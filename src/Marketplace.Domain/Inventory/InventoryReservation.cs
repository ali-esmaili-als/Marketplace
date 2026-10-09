using Marketplace.Domain.Common;

namespace Marketplace.Domain.Inventory;

public enum InventoryReservationStatus:byte { Active=1,Consumed=2,Released=3,Expired=4 }
public sealed class InventoryReservation:Entity<long>
{
    private InventoryReservation(){}
    public long ProductVariantId{get;private set;} public long OrderId{get;private set;} public long Quantity{get;private set;} public InventoryReservationStatus Status{get;private set;} public DateTime ExpiresAtUtc{get;private set;} public DateTime CreatedAtUtc{get;private set;}
    public static InventoryReservation Create(long id,long variantId,long orderId,long quantity,DateTime expiresAtUtc){if(id<=0||variantId<=0||orderId<=0||quantity<=0||expiresAtUtc<=DateTime.UtcNow)throw new DomainException("Invalid inventory reservation.");return new InventoryReservation{Id=id,ProductVariantId=variantId,OrderId=orderId,Quantity=quantity,Status=InventoryReservationStatus.Active,ExpiresAtUtc=expiresAtUtc,CreatedAtUtc=DateTime.UtcNow};}
    public void ExtendExpiry(DateTime expiresAtUtc){RequireActive();if(expiresAtUtc<=ExpiresAtUtc)throw new DomainException("New reservation expiry must be later.");ExpiresAtUtc=expiresAtUtc;}
    public void Consume(){RequireActive();Status=InventoryReservationStatus.Consumed;}
    public void Release(){RequireActive();Status=InventoryReservationStatus.Released;}
    public void Expire(DateTime now){RequireActive();if(now<ExpiresAtUtc)throw new DomainException("Reservation has not expired.");Status=InventoryReservationStatus.Expired;}
    private void RequireActive(){if(Status!=InventoryReservationStatus.Active)throw new DomainException("Inventory reservation is not active.");}
}