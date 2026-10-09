using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Complaints;
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
            if(payment.Status!=Marketplace.Domain.Payments.PaymentStatus.Succeeded)
                throw new DomainException("Only successfully paid orders can be refunded.");

            if(reason==RefundReason.AdminAdjustment || !Enum.IsDefined(reason))
                throw new DomainException("This refund reason is not available to customers.");

            if(order.Status==OrderStatus.DeliveryExpired && reason==RefundReason.DeliveryExpired)
                order.RequestRefund();
            else if(order.Status==OrderStatus.RefundRequested && reason==RefundReason.DeliveryExpired
                && order.DeliveredAtUtc is null && order.DeliveryExpiresAtUtc.HasValue)
            {
                // A prior refund attempt may have failed or been manually reconciled as not transferred.
                // Preserve the original delivery-expiry eligibility so the customer can retry.
            }
            else if(order.Status==OrderStatus.RefundRequested && reason==RefundReason.ComplaintCustomerWon)
            {
                var complaint=await _life.GetOpenComplaintByOrderAsync(orderId,token);
                if(complaint is null || complaint.Status!=ComplaintStatus.CustomerWon)
                    throw new DomainException("A customer-favorable resolved complaint is required for this refund reason.");
            }
            else
                throw new DomainException("Refund is allowed only after delivery expiry or a customer-favorable complaint resolution.");

            var existing=await _life.GetActiveRefundByOrderAsync(orderId,token);
            if(existing is not null)
                throw new DomainException("A refund is already in progress. Do not submit another request while its gateway result is uncertain.");

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
        var gateway=await _gatewayFactory.GetForExistingPaymentAsync(provider,ct);
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

            var bucket=BalanceBucket.Blocked;
            var bucketBefore=balance.BlockedIRR;
            if(balance.BlockedIRR>=order.SellerAmountIRR)
                balance.ConsumeBlock(order.SellerAmountIRR);
            else if(balance.PendingIRR>=order.SellerAmountIRR)
            {
                bucket=BalanceBucket.Pending;
                bucketBefore=balance.PendingIRR;
                balance.RemovePending(order.SellerAmountIRR);
            }
            else if(order.DeliveredAtUtc is null && order.DeliveryExpiresAtUtc.HasValue)
            {
                // Delivery expiry already removed the seller share from PendingIRR.
                // Do not debit it a second time when returning the customer's payment.
                bucket=BalanceBucket.Pending;
                bucketBefore=balance.PendingIRR;
            }
            else throw new DomainException("Seller balance does not contain the refundable seller amount.");

            hold.Consume();
            payment.MarkRefunded();
            order.MarkRefunded();

            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),order.SellerId,order.Id,null,
                BalanceTransactionType.Refund,order.SellerAmountIRR,
                bucketBefore,
                bucket==BalanceBucket.Blocked?balance.BlockedIRR:balance.PendingIRR,"REFUND",bucket));

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
    public Task ReconcileAsync(long refundId,long adminUserId,bool transferCompleted,string? bankReference,string note,CancellationToken ct=default)
        =>_uow.ExecuteInTransactionAsync(async token =>
        {
            if(string.IsNullOrWhiteSpace(note))
                throw new DomainException("A reconciliation note is required.");

            var refund=await _life.GetRefundAsync(refundId,token)??throw new DomainException("Refund not found.");
            if(refund.Status!=RefundStatus.Processing)
                throw new DomainException("Only refunds with an uncertain processing result can be reconciled.");

            if(!transferCompleted)
            {
                refund.Fail(note);
                _life.AddRefundReconciliationAudit(RefundReconciliationAudit.Create(refund.Id,adminUserId,false,note,null));
                await _uow.SaveChangesAsync(token);
                return 0;
            }

            if(string.IsNullOrWhiteSpace(bankReference))
                throw new DomainException("Bank reference is required when the refund transfer completed.");

            var order=await _orders.GetAsync(refund.OrderId,token)??throw new DomainException("Order not found.");
            var payment=await _payments.GetAsync(refund.PaymentId,token)??throw new DomainException("Payment not found.");
            if(order.Status!=OrderStatus.RefundRequested || payment.Status!=Marketplace.Domain.Payments.PaymentStatus.Succeeded)
                throw new DomainException("Order or payment is not in a refundable state.");

            refund.Complete(bankReference);
            var balance=await _life.GetSellerBalanceAsync(order.SellerId,token)??throw new DomainException("Seller balance not found.");
            var hold=await _life.GetActiveHoldByOrderAsync(order.Id,token)??throw new DomainException("Seller hold not found.");
            var bucket=BalanceBucket.Blocked;
            var bucketBefore=balance.BlockedIRR;
            if(balance.BlockedIRR>=order.SellerAmountIRR)
                balance.ConsumeBlock(order.SellerAmountIRR);
            else if(balance.PendingIRR>=order.SellerAmountIRR)
            {
                bucket=BalanceBucket.Pending;
                bucketBefore=balance.PendingIRR;
                balance.RemovePending(order.SellerAmountIRR);
            }
            else if(order.DeliveredAtUtc is null && order.DeliveryExpiresAtUtc.HasValue)
            {
                // Delivery expiry already removed the seller share from PendingIRR.
                // Do not debit it a second time when returning the customer's payment.
                bucket=BalanceBucket.Pending;
                bucketBefore=balance.PendingIRR;
            }
            else throw new DomainException("Seller balance does not contain the refundable seller amount.");

            hold.Consume();
            payment.MarkRefunded();
            order.MarkRefunded();
            _life.AddBalanceTransaction(BalanceTransaction.Create(
                await _ids.NextAsync(token),order.SellerId,order.Id,null,
                BalanceTransactionType.Refund,order.SellerAmountIRR,bucketBefore,
                bucket==BalanceBucket.Blocked?balance.BlockedIRR:balance.PendingIRR,
                "REFUND_RECONCILED",bucket));

            var commission=await _life.GetCommissionByOrderAsync(order.Id,token);
            if(commission is not null)
            {
                var reversed=Math.Min(commission.CommissionAmountIRR,refund.AmountIRR);
                _life.AddCommissionReversal(CommissionReversal.Create(
                    await _ids.NextAsync(token),commission.Id,order.Id,refund.Id,refund.AmountIRR,reversed));
            }
            _life.AddRefundReconciliationAudit(RefundReconciliationAudit.Create(refund.Id,adminUserId,true,note,bankReference));
            await _uow.SaveChangesAsync(token);
            return 0;
        },ct);

}
