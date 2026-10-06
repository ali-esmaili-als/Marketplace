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
        return services;
    }
}