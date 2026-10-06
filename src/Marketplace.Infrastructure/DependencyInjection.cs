using Marketplace.Application.Common.Abstractions;
using Marketplace.Infrastructure.Persistence;
using Marketplace.Application.Checkout.Ports;
using Marketplace.Infrastructure.Checkout;
using Marketplace.Application.Delivery.Ports;
using Marketplace.Application.Finance.Ports;
using Marketplace.Infrastructure.Delivery;
using Marketplace.Infrastructure.Finance;
using Marketplace.Application.Payments.Ports;
using Marketplace.Infrastructure.Payments;
using Marketplace.Application.Refunds.Ports;
using Marketplace.Infrastructure.Refunds;
using Marketplace.Application.Complaints.Ports;
using Marketplace.Infrastructure.Complaints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marketplace.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddMarketplaceInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<MarketplaceDbContext>(options =>
            options.UseSqlServer(configuration.GetConnectionString("Marketplace"), sql => sql.EnableRetryOnFailure(5)));
        services.AddScoped<IUnitOfWork, InfrastructureUnitOfWork>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddScoped<IExchangeRateProvider, SqlExchangeRateProvider>();
        services.AddSingleton<IIdGenerator, MonotonicIdGenerator>();
        services.AddScoped<ICheckoutReader, EfCheckoutReader>();
        services.AddScoped<IOrderWriter, EfOrderWriter>();
        services.AddScoped<IPaymentAttemptFactory, EfPaymentAttemptFactory>();
        services.AddScoped<IInventoryReservationService, EfInventoryReservationService>();
        services.AddScoped<ICouponReservationService, EfCouponReservationService>();
        services.AddScoped<IDeliveryService, EfDeliveryService>();
        services.AddScoped<IFinanceService, EfFinanceService>();
        services.AddScoped<IPaymentCompletionService, EfPaymentCompletionService>();
        services.AddScoped<ISettlementService, EfSettlementService>();
        services.AddScoped<IRefundService, EfRefundService>();
        services.AddScoped<IComplaintService, EfComplaintService>();
        return services;
    }

    private sealed class SystemClock : IClock
    {
        public DateTime UtcNow => DateTime.UtcNow;
    }

    private sealed class MonotonicIdGenerator : IIdGenerator
    {
        private static long _last;
        public long NewId()
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            while (true)
            {
                var current = Interlocked.Read(ref _last);
                var next = Math.Max(now, current + 1);
                if (Interlocked.CompareExchange(ref _last, next, current) == current)
                    return next;
            }
        }
    }
}