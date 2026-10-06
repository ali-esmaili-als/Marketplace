using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Marketplace.Application.Abstractions;
using Marketplace.Infrastructure.Payments;
using Marketplace.Infrastructure.Persistence;

namespace Marketplace.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMarketplaceInfrastructure(this IServiceCollection services,IConfiguration configuration)
    {
        var cs=configuration.GetConnectionString("Marketplace")??throw new InvalidOperationException("ConnectionStrings:Marketplace is missing.");
        services.AddDbContext<MarketplaceDbContext>(o=>o.UseSqlServer(cs,sql=>sql.EnableRetryOnFailure(5,TimeSpan.FromSeconds(10),null)));
        services.AddScoped<IUnitOfWork,EfUnitOfWork>();
        services.AddScoped<IIdGenerator,SqlIdGenerator>();
        services.AddScoped<IOrderRepository,OrderRepository>();
        services.AddScoped<IPaymentRepository,PaymentRepository>();
        services.AddScoped<ILifecycleRepository,LifecycleRepository>();
        services.AddScoped<ICartRepository,CartRepository>();
        services.AddScoped<ICatalogRepository,CatalogRepository>();
        services.AddScoped<IPaymentGateway,NotConfiguredPaymentGateway>();
        services.AddScoped<IPaymentProviderSettings,PaymentProviderSettingsRepository>();
        return services;
    }
}