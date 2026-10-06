using Marketplace.Application.Checkout.Ports;
using Marketplace.Application.Common.Abstractions;
using Marketplace.Domain.Orders;
using Marketplace.Infrastructure.Persistence;

namespace Marketplace.Infrastructure.Checkout;

public sealed class EfOrderWriter(MarketplaceDbContext db, IIdGenerator ids) : IOrderWriter
{
    public async Task<long> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        var id = request.Id;
        var number = $"ORD-{id}";
        var order = Order.Create(id, number, request.CustomerId, request.StoreId, request.CartId,
            request.SubTotalIRR, request.CampaignDiscountIRR, request.DirectDiscountIRR,
            request.CouponDiscountIRR, request.WarrantyAmountIRR, request.ShippingGrossIRR,
            request.ShippingBenefitIRR, request.ShippingAmountIRR, request.TotalAmountIRR,
            request.CouponId, request.CouponCodeSnapshot);

        foreach (var item in request.Items)
        {
            order.AddItem(OrderItem.Create(ids.NewId(), id, item.ProductId, item.ProductVariantId,
                item.ProductNameSnapshot, item.VariantKeySnapshot, item.SkuSnapshot, item.UnitPriceIRR,
                item.Quantity, item.LineSubtotalIRR, item.CampaignDiscountIRR, item.DirectDiscountIRR,
                item.CouponDiscountIRR, item.WarrantyId, item.WarrantyNameSnapshot, item.WarrantyAmountIRR,
                item.AllocatedShippingIRR, item.FinalLineTotalIRR, item.CampaignId, item.CampaignNameSnapshot));
        }

        db.Orders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        return id;
    }
}