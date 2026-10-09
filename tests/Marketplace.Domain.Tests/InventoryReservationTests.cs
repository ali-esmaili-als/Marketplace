using Marketplace.Domain.Common;
using Marketplace.Domain.Inventory;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class InventoryReservationTests
{
    [Fact]
    public void Active_reservation_can_be_extended_for_paid_delivery_window()
    {
        var initialExpiry = DateTime.UtcNow.AddHours(1);
        var reservation = InventoryReservation.Create(1, 2, 3, 4, initialExpiry);
        var deliveryExpiry = initialExpiry.AddDays(3);

        reservation.ExtendExpiry(deliveryExpiry);

        Assert.Equal(deliveryExpiry, reservation.ExpiresAtUtc);
        Assert.Equal(InventoryReservationStatus.Active, reservation.Status);
    }

    [Fact]
    public void Reservation_expiry_cannot_be_shortened_or_repeated()
    {
        var initialExpiry = DateTime.UtcNow.AddHours(1);
        var reservation = InventoryReservation.Create(1, 2, 3, 4, initialExpiry);

        Assert.Throws<DomainException>(() => reservation.ExtendExpiry(initialExpiry));
        Assert.Throws<DomainException>(() => reservation.ExtendExpiry(initialExpiry.AddMinutes(-1)));
    }

    [Fact]
    public void Consumed_reservation_cannot_be_extended()
    {
        var reservation = InventoryReservation.Create(1, 2, 3, 4, DateTime.UtcNow.AddHours(1));
        reservation.Consume();

        Assert.Throws<DomainException>(() => reservation.ExtendExpiry(DateTime.UtcNow.AddDays(3)));
    }
}