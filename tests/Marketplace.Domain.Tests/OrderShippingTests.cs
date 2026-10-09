using System;
using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Shipping;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class OrderShippingTests
{
    [Fact]
    public void Shipping_fee_is_snapshotted_and_added_after_discounts()
    {
        var order = Order.Create(1, 2, 3, 4, 1000, 1100, "checkout-request-key-01", 100);
        order.SetDiscounts(100, 0, null);
        Assert.Equal(100, order.ShippingFeeIRR);
        Assert.Equal(1100, order.TotalAmountIRR);
    }

    [Fact]
    public void Shipping_fee_is_not_commissionable_but_is_in_seller_proceeds()
    {
        var commission = Commission.Create(1, 2, 3, 4, 1100, 10m, 0, 100);
        Assert.Equal(1100, commission.OrderAmountIRR);
        Assert.Equal(100, commission.CommissionAmountIRR);
        Assert.Equal(1000, commission.SellerAmountIRR);
    }

    [Fact]
    public void Invalid_shipping_fee_and_delivery_estimate_are_rejected()
    {
        Assert.Throws<DomainException>(() => { StoreShippingRate.Create(1, 2, 3, -1, 3, 7, DateTime.UtcNow); });
        Assert.Throws<DomainException>(() => { StoreShippingRate.Create(1, 2, 3, 0, 8, 7, DateTime.UtcNow); });
        Assert.Throws<DomainException>(() => { Order.Create(1, 2, 3, 4, 1000, 1200, null, 100); });
    }
}
