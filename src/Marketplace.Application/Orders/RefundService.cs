using Marketplace.Application.Abstractions;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Refunds;

namespace Marketplace.Application.Orders;

public sealed class RefundService(IOrderRepository orders,IPaymentRepository payments,ILifecycleRepository life,IUnitOfWork uow,IIdGenerator ids,IPaymentGateway gateway)
{
    public Task ProcessAsync(long orderId,RefundReason reason,CancellationToken ct=default)=>uow.ExecuteInTransactionAsync(async token=>{
        var o=await orders.GetAsync(orderId,token)??throw new DomainException("Order not found.");var p=await payments.GetByOrderAsync(orderId,token)??throw new DomainException("Payment not found.");
        var existing=await life.GetActiveRefundByOrderAsync(orderId,token);if(existing!=null)throw new DomainException("An active refund already exists.");
        var r=Refund.Create(await ids.NextAsync(token),o.Id,p.Id,o.CustomerId,o.TotalAmountIRR,reason);life.AddRefund(r);r.Approve();r.StartProcessing();await uow.SaveChangesAsync(token);
        var ok=await gateway.RefundAsync(p.ReferenceNumber,r.AmountIRR,token);if(!ok){r.Fail("Payment gateway refund failed.");await uow.SaveChangesAsync(token);throw new DomainException("Refund gateway failed.");}
        r.Complete(null);var b=await life.GetSellerBalanceAsync(o.SellerId,token)??throw new DomainException("Seller balance not found.");var h=await life.GetActiveHoldByOrderAsync(o.Id,token)??throw new DomainException("Seller hold not found.");
        if(b.BlockedIRR>=o.SellerAmountIRR)b.ConsumeBlock(o.SellerAmountIRR);else if(b.PendingIRR>=o.SellerAmountIRR)b.RemovePending(o.SellerAmountIRR);
        h.Consume();p.MarkRefunded();o.MarkRefunded();
        var commission=await life.GetCommissionByOrderAsync(o.Id,token);
        if(commission!=null){var reversed=Math.Min(commission.CommissionAmountIRR,r.AmountIRR);life.AddCommissionReversal(CommissionReversal.Create(await ids.NextAsync(token),commission.Id,o.Id,r.Id,r.AmountIRR,reversed));}
        await uow.SaveChangesAsync(token);return 0;},ct);
}