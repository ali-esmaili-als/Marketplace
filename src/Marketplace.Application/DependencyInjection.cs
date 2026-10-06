using Microsoft.Extensions.DependencyInjection;
using Marketplace.Application.Orders;
using Marketplace.Application.Cart;

namespace Marketplace.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddMarketplaceApplication(this IServiceCollection services)
    {
        services.AddScoped<OrderLifecycleService>();
        services.AddScoped<RefundService>();
        services.AddScoped<CartService>();
        services.AddScoped<OrderCreationService>();
        services.AddScoped<PaymentVerificationService>();
        services.AddScoped<Settlements.SettlementService>();
        services.AddScoped<Payments.PaymentProviderSettingsService>();
        services.AddScoped<Shipping.ShippingCoverageService>();
        services.AddScoped<Identity.AuthenticationService>();
        services.AddScoped<Identity.RegistrationService>();
        services.AddScoped<Identity.PermissionService>();
        services.AddScoped<Identity.IdentityAdminService>();
        services.AddScoped<Sellers.SellerManagementService>();
        services.AddScoped<Pricing.PricingService>();
        services.AddScoped<Pricing.PricingManagementService>();
        services.AddScoped<Catalog.CatalogManagementService>();
        services.AddScoped<OrderQueryService>();
        return services;
    }
}