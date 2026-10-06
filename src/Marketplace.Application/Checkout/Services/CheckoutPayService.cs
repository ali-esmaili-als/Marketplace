using Marketplace.Application.Checkout.Commands;
using Marketplace.Application.Checkout.Ports;
using Marketplace.Application.Checkout.Results;
using Marketplace.Application.Common.Abstractions;

namespace Marketplace.Application.Checkout.Services;

public sealed class CheckoutPayService(
    ICurrentUser currentUser,
    ICheckoutReader reader,
    ICheckoutPricingService pricing,
    IExchangeRateProvider exchangeRates,
    IOrderWriter orders,
    IInventoryReservationService inventory,
    ICouponReservationService coupons,
    IPaymentAttemptFactory payments,
    IUnitOfWork unitOfWork)
{
    public async Task<CheckoutPayResult> ExecuteAsync(CheckoutPayCommand command, CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated) throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(command.CurrencyCode)) throw new ArgumentException("Currency is required.", nameof(command));

        var cart = await reader.GetCartAsync(command.CartId, currentUser.UserId, cancellationToken)
            ?? throw new InvalidOperationException("Cart not found.");
        if (cart.Items.Count == 0) throw new InvalidOperationException("Cart is empty.");

        // Coupon reservation requires a real OrderId. Therefore coupon reservation is
        // intentionally deferred until the Order exists; a follow-up reconciliation
        // step must bind the reservation to that Order inside the same transaction.
        var basePricing = await pricing.CalculateAsync(cart, 0, cancellationToken);
        var rate = await exchangeRates.GetLatestAsync(command.CurrencyCode.Trim().ToUpperInvariant(), cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var finalPricing = basePricing;
            var orderItems = finalPricing.Items.Select(x => new CreateOrderItemRequest(
                x.ProductId, x.ProductVariantId, x.ProductNameSnapshot, x.VariantKeySnapshot,
                x.SkuSnapshot, x.UnitPriceIRR, x.Quantity, x.LineSubtotalIRR,
                x.CampaignDiscountIRR, x.DirectDiscountIRR, x.CouponDiscountIRR,
                x.WarrantyId, x.WarrantyNameSnapshot, x.WarrantyAmountIRR,
                x.AllocatedShippingIRR, x.FinalLineTotalIRR, x.CampaignId,
                x.CampaignNameSnapshot)).ToArray();

            var orderId = await orders.CreateAsync(new CreateOrderRequest(
                cart.CustomerId, cart.StoreId, cart.CartId,
                finalPricing.SubTotalIRR, finalPricing.CampaignDiscountIRR,
                finalPricing.DirectDiscountIRR, finalPricing.CouponDiscountIRR,
                finalPricing.WarrantyAmountIRR, finalPricing.ShippingGrossIRR,
                finalPricing.ShippingBenefitIRR, finalPricing.ShippingAmountIRR,
                finalPricing.TotalAmountIRR, null, null, orderItems), cancellationToken);

            if (!string.IsNullOrWhiteSpace(command.CouponCode))
                throw new InvalidOperationException("Coupon checkout binding is not enabled yet.");

            await inventory.ReserveAsync(orderId,
                cart.Items.Select(x => new InventoryReservationRequest(
                    x.ProductId, x.ProductVariantId, x.Quantity)).ToArray(), cancellationToken);

            var paymentAttemptId = await payments.CreateAsync(
                new PaymentAttemptRequest(orderId, cart.CustomerId, finalPricing.TotalAmountIRR,
                    rate.CurrencyCode, rate.RateToIRR, "PendingGateway"), cancellationToken);

            await unitOfWork.CommitTransactionAsync(cancellationToken);
            return new CheckoutPayResult(orderId, paymentAttemptId, finalPricing.TotalAmountIRR, rate.CurrencyCode, rate.RateToIRR);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}