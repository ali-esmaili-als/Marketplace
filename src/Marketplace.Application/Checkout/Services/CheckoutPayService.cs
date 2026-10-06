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
    IIdGenerator ids,
    IUnitOfWork unitOfWork)
{
    public async Task<CheckoutPayResult> ExecuteAsync(
        CheckoutPayCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException();

        if (string.IsNullOrWhiteSpace(command.CurrencyCode))
            throw new ArgumentException("Currency is required.", nameof(command));

        var cart = await reader.GetCartAsync(
            command.CartId, currentUser.UserId, cancellationToken)
            ?? throw new InvalidOperationException("Cart not found.");

        if (cart.Items.Count == 0)
            throw new InvalidOperationException("Cart is empty.");

        var basePricing = await pricing.CalculateAsync(cart, 0, cancellationToken);
        var rate = await exchangeRates.GetLatestAsync(
            command.CurrencyCode.Trim().ToUpperInvariant(), cancellationToken);

        await unitOfWork.BeginTransactionAsync(cancellationToken);

        long orderId = 0;
        try
        {
            orderId = ids.NewId();

            CouponReservationResult? coupon = null;
            if (!string.IsNullOrWhiteSpace(command.CouponCode))
            {
                var eligibleAmount = Math.Max(
                    0,
                    basePricing.SubTotalIRR -
                    basePricing.CampaignDiscountIRR -
                    basePricing.DirectDiscountIRR);

                coupon = await coupons.ReserveAsync(
                    cart.CustomerId,
                    cart.CartId,
                    orderId,
                    command.CouponCode,
                    eligibleAmount,
                    basePricing.CampaignDiscountIRR > 0,
                    cancellationToken);

                if (coupon is null)
                    throw new InvalidOperationException("Coupon is invalid, expired, unavailable, or not applicable.");
            }

            var finalPricing = await pricing.CalculateAsync(
                cart,
                coupon?.DiscountIRR ?? 0,
                cancellationToken);

            var orderItems = finalPricing.Items.Select(x => new CreateOrderItemRequest(
                x.ProductId,
                x.ProductVariantId,
                x.ProductNameSnapshot,
                x.VariantKeySnapshot,
                x.SkuSnapshot,
                x.UnitPriceIRR,
                x.Quantity,
                x.LineSubtotalIRR,
                x.CampaignDiscountIRR,
                x.DirectDiscountIRR,
                x.CouponDiscountIRR,
                x.WarrantyId,
                x.WarrantyNameSnapshot,
                x.WarrantyAmountIRR,
                x.AllocatedShippingIRR,
                x.FinalLineTotalIRR,
                x.CampaignId,
                x.CampaignNameSnapshot)).ToArray();

            await orders.CreateAsync(
                new CreateOrderRequest(
                    orderId,
                    cart.CustomerId,
                    cart.StoreId,
                    cart.CartId,
                    finalPricing.SubTotalIRR,
                    finalPricing.CampaignDiscountIRR,
                    finalPricing.DirectDiscountIRR,
                    finalPricing.CouponDiscountIRR,
                    finalPricing.WarrantyAmountIRR,
                    finalPricing.ShippingGrossIRR,
                    finalPricing.ShippingBenefitIRR,
                    finalPricing.ShippingAmountIRR,
                    finalPricing.TotalAmountIRR,
                    coupon?.CouponId,
                    coupon?.CouponCode,
                    orderItems),
                cancellationToken);

            await inventory.ReserveAsync(
                orderId,
                cart.Items.Select(x => new InventoryReservationRequest(
                    x.ProductId,
                    x.ProductVariantId,
                    x.Quantity)).ToArray(),
                cancellationToken);

            var paymentAttemptId = await payments.CreateAsync(
                new PaymentAttemptRequest(
                    orderId,
                    cart.CustomerId,
                    finalPricing.TotalAmountIRR,
                    rate.CurrencyCode,
                    rate.RateToIRR,
                    "PendingGateway"),
                cancellationToken);

            await unitOfWork.CommitTransactionAsync(cancellationToken);

            return new CheckoutPayResult(
                orderId,
                paymentAttemptId,
                finalPricing.TotalAmountIRR,
                rate.CurrencyCode,
                rate.RateToIRR);
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(cancellationToken);
            throw;
        }
    }
}
