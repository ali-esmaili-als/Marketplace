using System.Text.Json;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Complaints;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
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

    public RefundService(IOrderRepository orders, IPaymentRepository payments, ILifecycleRepository life,
        IUnitOfWork uow, IIdGenerator ids, IPaymentGatewayFactory gatewayFactory)
    {
        _orders = orders;
        _payments = payments;
        _life = life;
        _uow = uow;
        _ids = ids;
        _gatewayFactory = gatewayFactory;
    }

    public async Task ProcessAsync(long orderId, RefundReason reason, CancellationToken ct = default)
    {
        long refundId = 0;
        long paymentId = 0;
        long amount = 0;
        string? paymentReference = null;
        PaymentProviderCode providerCode = default;

        // Reserve the refund in SQL and commit before calling the external gateway.
        await _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            var order = await _orders.GetAsync(orderId, token)
                ?? throw new DomainException("Order not found.");
            var payment = await _payments.GetByOrderAsync(orderId, token)
                ?? throw new DomainException("Payment not found.");

            if (payment.Status != PaymentStatus.Succeeded)
                throw new DomainException("Only successfully paid orders can be refunded.");

            // Validate the configured provider before reserving the refund. No bank request has
            // been issued at this point, so an invalid provider must not strand a refund in Processing.
            if (!Enum.TryParse<PaymentProviderCode>(payment.Provider, true, out providerCode)
                || !Enum.IsDefined(typeof(PaymentProviderCode), providerCode))
                throw new DomainException("Invalid payment provider; refund was not reserved.");

            if (reason == RefundReason.AdminAdjustment || !Enum.IsDefined(reason))
                throw new DomainException("This refund reason is not available to customers.");

            if (order.Status == OrderStatus.DeliveryExpired && reason == RefundReason.DeliveryExpired)
                order.RequestRefund();
            else if (order.Status == OrderStatus.RefundRequested && reason == RefundReason.DeliveryExpired
                && order.DeliveredAtUtc is null && order.DeliveryExpiresAtUtc.HasValue)
            {
                // Preserve delivery-expiry eligibility after a failed or manually reconciled attempt.
            }
            else if (order.Status == OrderStatus.RefundRequested && reason == RefundReason.ComplaintCustomerWon)
            {
                var complaint = await _life.GetOpenComplaintByOrderAsync(orderId, token);
                if (complaint is null || complaint.Status != ComplaintStatus.CustomerWon)
                    throw new DomainException("A customer-favorable resolved complaint is required for this refund reason.");
            }
            else
                throw new DomainException("Refund is allowed only after delivery expiry or a customer-favorable complaint resolution.");

            var existing = await _life.GetActiveRefundByOrderAsync(orderId, token);
            if (existing is not null)
                throw new DomainException("A refund is already in progress. Do not submit another request while its gateway result is uncertain.");

            var refund = Refund.Create(await _ids.NextAsync(token), order.Id, payment.Id,
                order.CustomerId, order.TotalAmountIRR, reason);
            refund.Approve();
            refund.StartProcessing();
            _life.AddRefund(refund);

            refundId = refund.Id;
            paymentId = payment.Id;
            amount = refund.AmountIRR;
            paymentReference = payment.ReferenceNumber;

            await _uow.SaveChangesAsync(token);
            return 0;
        }, ct);

        // Never keep a database transaction open while calling an external payment provider.
        IPaymentGateway gateway;
        try
        {
            // Factory resolution/configuration completes before any refund request is sent to the bank.
            gateway = await _gatewayFactory.GetForExistingPaymentAsync(providerCode, ct);
        }
        catch
        {
            // No gateway instance was returned, therefore no bank refund call was issued. Release the
            // active-refund guard by recording a definitive local failure; never do this around RefundAsync.
            try
            {
                await _uow.ExecuteInSerializableTransactionAsync(async token =>
                {
                    var refund = await _life.GetRefundAsync(refundId, token);
                    if (refund is not null && refund.Status == RefundStatus.Processing)
                    {
                        refund.Fail("Payment gateway could not be initialized; no refund request was sent.");
                        await _uow.SaveChangesAsync(token);
                    }
                    return 0;
                }, CancellationToken.None);
            }
            catch
            {
                // Preserve the original initialization exception. If persistence is unavailable, the
                // reserved Processing row remains visible for audited manual reconciliation.
            }
            throw;
        }

        // An exception/timeout from RefundAsync is intentionally not converted to Failed: the bank outcome may be unknown.
        var gatewayResult = await gateway.RefundAsync(paymentReference, amount, ct);

        await _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            var order = await _orders.GetAsync(orderId, token)
                ?? throw new DomainException("Order not found.");
            var payment = await _payments.GetAsync(paymentId, token)
                ?? throw new DomainException("Payment not found.");
            var refund = await _life.GetRefundAsync(refundId, token)
                ?? throw new DomainException("Refund not found.");

            if (refund.Status == RefundStatus.Completed)
                return 0;

            if (!gatewayResult.IsOutcomeDefinitive || (gatewayResult.IsSuccessful && string.IsNullOrWhiteSpace(gatewayResult.Reference)))
            {
                // A positive boolean alone is not durable evidence of a completed refund. Keep the
                // refund in Processing so the active-refund guard prevents a duplicate bank request.
                _life.AddOutboxMessage(OutboxMessage.Create(
                    await _ids.NextAsync(token),
                    "Refund.ReconciliationRequired",
                    JsonSerializer.Serialize(new { refund.Id, refund.OrderId, refund.PaymentId, refund.AmountIRR,
                        Reason = !gatewayResult.IsOutcomeDefinitive ? "Provider outcome is ambiguous." : "Provider reported success without a refund reference." })));
                await _uow.SaveChangesAsync(token);
                return 0;
            }

            if (!gatewayResult.IsSuccessful)
            {
                refund.Fail(gatewayResult.Error ?? "Payment gateway refund failed.");
                await _uow.SaveChangesAsync(token);
                return 0;
            }

            await ApplySuccessfulRefundAsync(refund, order, payment, gatewayResult.Reference, token);
            await _uow.SaveChangesAsync(token);
            return 0;
        }, ct);
    }

    public Task ReconcileAsync(long refundId, long adminUserId, bool transferCompleted,
        string? bankReference, string note, CancellationToken ct = default)
        => _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            if (string.IsNullOrWhiteSpace(note))
                throw new DomainException("A reconciliation note is required.");

            var refund = await _life.GetRefundAsync(refundId, token)
                ?? throw new DomainException("Refund not found.");
            if (refund.Status != RefundStatus.Processing)
                throw new DomainException("Only refunds with an uncertain processing result can be reconciled.");

            if (!transferCompleted)
            {
                refund.Fail(note);
                _life.AddRefundReconciliationAudit(
                    RefundReconciliationAudit.Create(refund.Id, adminUserId, false, note, null));
                await _uow.SaveChangesAsync(token);
                return 0;
            }

            if (string.IsNullOrWhiteSpace(bankReference))
                throw new DomainException("Bank reference is required when the refund transfer completed.");

            var order = await _orders.GetAsync(refund.OrderId, token)
                ?? throw new DomainException("Order not found.");
            var payment = await _payments.GetAsync(refund.PaymentId, token)
                ?? throw new DomainException("Payment not found.");

            if (order.Status != OrderStatus.RefundRequested || payment.Status != PaymentStatus.Succeeded)
                throw new DomainException("Order or payment is not in a refundable state.");

            // Keep manual reconciliation and a confirmed gateway success on exactly the same
            // ledger/balance/hold/commission path so their financial effects cannot drift.
            await ApplySuccessfulRefundAsync(refund, order, payment, bankReference, token);
            _life.AddRefundReconciliationAudit(
                RefundReconciliationAudit.Create(refund.Id, adminUserId, true, note, bankReference));
            await _uow.SaveChangesAsync(token);
            return 0;
        }, ct);

    private async Task ApplySuccessfulRefundAsync(Refund refund, Order order, Payment payment,
        string? bankReference, CancellationToken token)
    {
        // Preflight all order/payment/refund/hold/balance invariants before mutating any aggregate.
        // The SQL transaction rolls back on failure, but validating first also protects callers
        // and tests that use non-transactional repositories or mocks.
        if (refund.Status != RefundStatus.Processing)
            throw new DomainException("Refund must be processing before finalization.");
        if (refund.OrderId != order.Id || refund.PaymentId != payment.Id
            || refund.CustomerId != order.CustomerId || refund.AmountIRR != order.TotalAmountIRR)
            throw new DomainException("Refund does not match the order and payment.");
        if (order.Status != OrderStatus.RefundRequested)
            throw new DomainException("Order is not awaiting refund.");
        if (payment.OrderId != order.Id || payment.CustomerId != order.CustomerId
            || payment.AmountIRR != order.TotalAmountIRR || payment.Status != PaymentStatus.Succeeded)
            throw new DomainException("Payment is not eligible for this refund.");

        var balance = await _life.GetSellerBalanceAsync(order.SellerId, token)
            ?? throw new DomainException("Seller balance not found.");
        var hold = await _life.GetActiveHoldByOrderAsync(order.Id, token)
            ?? throw new DomainException("Seller hold not found.");
        if (balance.SellerId != order.SellerId
            || hold.SellerId != order.SellerId
            || hold.OrderId != order.Id
            || hold.AmountIRR != order.SellerAmountIRR
            || hold.Status != BalanceHoldStatus.Active)
            throw new DomainException("Seller balance hold does not match the order and seller.");
        if (order.SellerAmountIRR <= 0)
            throw new DomainException("Seller share must be positive before refund finalization.");

        var bucket = BalanceBucket.Blocked;
        var bucketBefore = balance.BlockedIRR;
        var balanceDebited = true;

        if (balance.BlockedIRR >= order.SellerAmountIRR)
        {
            // Prefer the held seller share when it is still in the blocked bucket.
        }
        else if (balance.PendingIRR >= order.SellerAmountIRR)
        {
            bucket = BalanceBucket.Pending;
            bucketBefore = balance.PendingIRR;
        }
        else if (order.DeliveredAtUtc is null && order.DeliveryExpiresAtUtc.HasValue)
        {
            // Delivery expiry already removed the seller share from PendingIRR.
            // Do not debit it twice when returning the customer's payment.
            bucket = BalanceBucket.Pending;
            bucketBefore = balance.PendingIRR;
            balanceDebited = false;
        }
        else
            throw new DomainException("Seller balance does not contain the refundable seller amount.");

        // No domain mutation occurs before every cross-aggregate precondition is verified.
        refund.Complete(bankReference);
        if (balanceDebited)
        {
            if (bucket == BalanceBucket.Blocked)
                balance.ConsumeBlock(order.SellerAmountIRR);
            else
                balance.RemovePending(order.SellerAmountIRR);
        }
        hold.Consume();
        payment.MarkRefunded();
        order.MarkRefunded();

        _life.AddBalanceTransaction(BalanceTransaction.Create(
            await _ids.NextAsync(token), order.SellerId, order.Id, null,
            BalanceTransactionType.Refund, balanceDebited ? order.SellerAmountIRR : 0,
            bucketBefore, bucket == BalanceBucket.Blocked ? balance.BlockedIRR : balance.PendingIRR,
            balanceDebited ? "REFUND" : "REFUND_SELLER_SHARE_ALREADY_REMOVED", bucket, refund.Id));

        var commission = await _life.GetCommissionByOrderAsync(order.Id, token);
        if (commission is not null)
        {
            var reversed = Math.Min(commission.CommissionAmountIRR, refund.AmountIRR);
            _life.AddCommissionReversal(CommissionReversal.Create(
                await _ids.NextAsync(token), commission.Id, order.Id, refund.Id, refund.AmountIRR, reversed));
        }
    }
}
