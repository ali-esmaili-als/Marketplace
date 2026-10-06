using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Refunds;

namespace Marketplace.Application.Orders;

public sealed class RefundService
{
    private readonly IOrderRepository _orders;
    private readonly IPaymentRepository _payments;
    private readonly ILifecycleRepository _life;
    private readonly IUnitOfWork _uow;
    private readonly IIdGenerator _ids;
    private readonly IPaymentGatewayFactory _gatewayFactory;

    public RefundService(IOrderRepository orders,IPaymentRepository payments,ILifecycleRepository life,
        IUnitOfWork uow,IIdGenerator ids,IPaymentGatewayFactory gatewayFactory)
    {
        _orders=orders; _payments=payments; _life=life; _uow=uow; _ids=ids; _gatewayFactory=gatewayFactory;
    }

    public async Task ProcessAsync(long orderId,RefundReason reason,CancellationToken ct=default)
    {
        long refundId=0;
        long paymentId=0;
        long amount=0;
        string? paymentReference=null;

        // Phase 1: reserve the refund in SQL and commit before calling the external gateway.
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            var order=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
            var payment=await _payments.GetByOrderAsync(orderId,token)??throw new DomainException("Payment not found.");
            var existing=await _life.GetActiveRefundByOrderAsync(orderId,token);

            if(existing is not null)
            {
                refundId=existing.Id;
                paymentId=existing.PaymentId;
                amount=existing.AmountIRR;
                paymentReference=payment.ReferenceNumber;
                return 0;
            }

            var refund=Refund.Create(await _ids.NextAsync(token),order.Id,payment.Id,order.CustomerId,order.TotalAmountIRR,reason);
            refund.Approve();
            refund.StartProcessing();
            _life.AddRefund(refund);

            refundId=refund.Id;
            paymentId=payment.Id;
            amount=refund.AmountIRR;
            paymentReference=payment.ReferenceNumber;

            await _uow.SaveChangesAsync(token);
            return 0;
        },ct);

        // The gateway call is intentionally outside the DB transaction.
        var paymentForGateway=await _payments.GetAsync(paymentId,ct)??throw new DomainException("Payment not found.");
        if(!Enum.TryParse<Marketplace.Domain.Payments.PaymentProviderCode>(paymentForGateway.Provider,true,out var provider)) throw new DomainException("Invalid payment provider.");
        var gateway=await _gatewayFactory.GetAsync(provider,ct);
        var gatewayOk=await gateway.RefundAsync(paymentReference,amount,ct);

        // Phase 2: finalize exactly once in a short DB transaction.
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            var order=await _orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");
            var payment=await _payments.GetAsync(paymentId,token)??throw new DomainException("Payment not found.");
            var refund=await _life.GetRefundAsync(refundId,token)??throw new DomainException("Refund not found.");

            if(refund.Status==RefundStatus.Completed) return 0;

            if(!gatewayOk)
            {
                refund.Fail("Payment gateway refund failed.");
                await _uow.SaveChangesAsync(token);
                return 0;
            }

            refund.Complete(null);
            var balance=await _life.GetSellerBalanceAsync(order.SellerId,token)??throw new DomainException("Seller balance not found.");
            var hold=await _life.GetActiveHoldByOrderAsync(order.Id,token)??throw new DomainException("Seller hold not found.");

            var beforeBlocked=balance.BlockedIRR;
            var beforePending=balance.PendingIRR;

            if(balance.BlockedIRR>=order.SellerAmountIRR)
                balance.ConsumeBlock(order.SellerAmountIRR);
            else if(balance.PendingIRR>=order.SellerAmountIRR)
                balance.RemovePending(order.SellerAmountIRR);
            else
                throw new DomainException("Seller balance does not contain the refundable seller amount.");

            hold.Consume();
            payment.MarkRefunded();
            order.MarkRefunded();

            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),order.SellerId,order.Id,null,
                BalanceTransactionType.Refund,order.SellerAmountIRR,
                Math.Max(beforeBlocked, beforePending)-order.SellerAmountIRR,
                Math.Max(balance.BlockedIRR,balance.PendingIRR),"REFUND"));

            var commission=await _life.GetCommissionByOrderAsync(order.Id,token);
            if(commission is not null)
            {
                var reversed=Math.Min(commission.CommissionAmountIRR,refund.AmountIRR);
                _life.AddCommissionReversal(CommissionReversal.Create(
                    await _ids.NextAsync(token),commission.Id,order.Id,refund.Id,refund.AmountIRR,reversed));
            }

            await _uow.SaveChangesAsync(token);
            return 0;
        },ct);
    }
}
