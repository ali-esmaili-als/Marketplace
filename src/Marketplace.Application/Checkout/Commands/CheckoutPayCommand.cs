namespace Marketplace.Application.Checkout.Commands; public sealed record CheckoutPayCommand(long CartId,string? CouponCode,string CurrencyCode);
