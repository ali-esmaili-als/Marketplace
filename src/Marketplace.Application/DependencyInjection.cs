using Microsoft.Extensions.DependencyInjection;
using Marketplace.Application.Orders;

namespace Marketplace.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddMarketplaceApplication(this IServiceCollection services)
    {
        services.AddScoped<OrderLifecycleService>();
        services.AddScoped<RefundService>();
        return services;
    }
}