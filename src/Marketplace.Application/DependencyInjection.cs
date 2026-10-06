using Marketplace.Application.Checkout.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddMarketplaceApplication(this IServiceCollection services)
    {
        services.AddScoped<ICheckoutPricingService, CheckoutPricingService>();
        return services;
    }
}