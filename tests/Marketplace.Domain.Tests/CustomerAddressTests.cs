using Marketplace.Domain.Common;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Shipping;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class CustomerAddressTests
{
    [Fact]
    public void Create_StoresRecipientAndDeliveryDetails()
    {
        var address = CustomerAddress.Create(10, 20, 30, "Ali Customer", "09120000000",
            "خیابان نمونه، پلاک ۱۲", "1234567890", "طبقه دوم", true);

        Assert.Equal(20, address.CustomerId);
        Assert.Equal(30, address.CityId);
        Assert.Equal("Ali Customer", address.RecipientName);
        Assert.True(address.IsDefault);
        Assert.Equal("طبقه دوم", address.DeliveryNote);
    }

    [Theory]
    [InlineData("", "09120000000", "خیابان نمونه", "1234567890")]
    [InlineData("Ali", "12", "خیابان نمونه", "1234567890")]
    [InlineData("Ali", "09120000000", "خیابان", "1234567890")]
    [InlineData("Ali", "09120000000", "خیابان نمونه", "123")]
    public void Create_RejectsIncompleteAddress(string name, string mobile, string line, string postal)
    {
        Assert.Throws<DomainException>(() => CustomerAddress.Create(10, 20, 30, name, mobile, line, postal, null, false));
    }

    [Fact]
    public void Update_ChangesCityAndRecipientDetails()
    {
        var address = CustomerAddress.Create(10, 20, 30, "Ali Customer", "09120000000",
            "خیابان نمونه، پلاک ۱۲", "1234567890", null, true);

        address.Update(31, "Sara Customer", "09121111111", "خیابان جدید، پلاک ۳", "9876543210", null, false);

        Assert.Equal(31, address.CityId);
        Assert.Equal("Sara Customer", address.RecipientName);
        Assert.False(address.IsDefault);
    }
}

public sealed class OrderDeliveryAddressTests
{
    [Fact]
    public void SetDeliveryAddress_SnapshotsRecipientAndAddress()
    {
        var order = Order.Create(1, 2, 3, 4, 1000, 1100, "request-key-123456", 100);
        order.SetShippingDestination(5, "تهران", "تهران");
        order.SetDeliveryAddress("Ali Customer", "09120000000", "خیابان نمونه، پلاک ۱۲", "1234567890", "طبقه دوم");

        Assert.Equal("Ali Customer", order.DeliveryRecipientNameSnapshot);
        Assert.Equal("09120000000", order.DeliveryRecipientMobileSnapshot);
        Assert.Equal("خیابان نمونه، پلاک ۱۲", order.DeliveryAddressLineSnapshot);
        Assert.Equal("1234567890", order.DeliveryPostalCodeSnapshot);
        Assert.Equal("طبقه دوم", order.DeliveryNoteSnapshot);
    }

    [Fact]
    public void SetDeliveryAddress_RejectsMissingRequiredFields()
    {
        var order = Order.Create(1, 2, 3, 4, 1000, 1100, "request-key-123456", 100);
        Assert.Throws<DomainException>(() => order.SetDeliveryAddress("", "09120000000", "خیابان نمونه", "1234567890", null));
    }
}
