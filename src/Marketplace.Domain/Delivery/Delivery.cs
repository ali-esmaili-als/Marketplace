using Marketplace.Domain.Common;

namespace Marketplace.Domain.Delivery;

public sealed class Delivery:AggregateRoot<long>
{
    private Delivery(){}
    public long OrderId{get;private set;} public long SellerId{get;private set;} public DeliveryStatus Status{get;private set;} public DateTime? ReadyAtUtc{get;private set;} public DateTime? DeliveredAtUtc{get;private set;} public DateTime ExpiresAtUtc{get;private set;} public string? ConfirmationReference{get;private set;}
    public static Delivery Create(long id,long orderId,long sellerId,DateTime expiresAtUtc){if(id<=0||orderId<=0||sellerId<=0||expiresAtUtc<=DateTime.UtcNow)throw new DomainException("Invalid delivery.");return new Delivery{Id=id,OrderId=orderId,SellerId=sellerId,Status=DeliveryStatus.Pending,ExpiresAtUtc=expiresAtUtc};}
    public void MarkReady(){if(Status!=DeliveryStatus.Pending)throw new DomainException("Delivery cannot become ready.");Status=DeliveryStatus.Ready;ReadyAtUtc=DateTime.UtcNow;}
    public void ConfirmDelivered(string reference,DateTime now){if(Status!=DeliveryStatus.Ready)throw new DomainException("Delivery is not ready.");if(now>ExpiresAtUtc)throw new DomainException("Delivery has expired.");if(string.IsNullOrWhiteSpace(reference))throw new DomainException("Confirmation reference is required.");Status=DeliveryStatus.Delivered;ConfirmationReference=reference.Trim();DeliveredAtUtc=now;}
    public void Expire(DateTime now){if(Status!=DeliveryStatus.Ready)throw new DomainException("Delivery is not awaiting delivery.");if(now<ExpiresAtUtc)throw new DomainException("Delivery has not expired.");Status=DeliveryStatus.Expired;}
    public void Cancel(){if(Status==DeliveryStatus.Delivered)throw new DomainException("Delivered delivery cannot be cancelled.");Status=DeliveryStatus.Cancelled;}
}