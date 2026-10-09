using Marketplace.Domain.Common;

namespace Marketplace.Domain.Orders;

public sealed class Order : AggregateRoot<long>
{
    private Order() { }
    public long CustomerId { get; private set; }
    public string? RequestKey { get; private set; }
    public long SellerId { get; private set; }
    public long StoreId { get; private set; }
    public long SubtotalAmountIRR { get; private set; }
    public long CampaignDiscountIRR { get; private set; }
    public long CouponDiscountIRR { get; private set; }
    public long ShippingFeeIRR { get; private set; }
    public string? CouponCodeSnapshot { get; private set; }
    public long TotalAmountIRR { get; private set; }
    public long SellerAmountIRR { get; private set; }
    public long? DestinationCityId { get; private set; }
    public string? DestinationCityNameSnapshot { get; private set; }
    public string? DestinationProvinceNameSnapshot { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public DateTime? DeliveredAtUtc { get; private set; }
    public DateTime? DeliveryExpiresAtUtc { get; private set; }
    public DateTime? ComplaintExpiresAtUtc { get; private set; }

    public static Order Create(long id,long customerId,long sellerId,long storeId,long subtotalAmountIrr,long totalAmountIrr,string? requestKey=null,long shippingFeeIrr=0)
    {
        if(id<=0||customerId<=0||sellerId<=0||storeId<=0||subtotalAmountIrr<=0||shippingFeeIrr<0||totalAmountIrr<=0||totalAmountIrr>checked(subtotalAmountIrr+shippingFeeIrr))
            throw new DomainException("Invalid order.");
        if (!string.IsNullOrWhiteSpace(requestKey) && (requestKey.Trim().Length > 64 || requestKey.Trim().Length < 16)) throw new DomainException("Invalid checkout request key.");
        return new Order { Id=id,CustomerId=customerId,RequestKey=string.IsNullOrWhiteSpace(requestKey)?null:requestKey.Trim(),SellerId=sellerId,StoreId=storeId,SubtotalAmountIRR=subtotalAmountIrr,ShippingFeeIRR=shippingFeeIrr,TotalAmountIRR=totalAmountIrr,SellerAmountIRR=totalAmountIrr,Status=OrderStatus.PendingPayment,CreatedAtUtc=DateTime.UtcNow };
    }
    public void SetDiscounts(long campaignDiscountIrr,long couponDiscountIrr,string? couponCode)
    {
        if(campaignDiscountIrr<0||couponDiscountIrr<0||campaignDiscountIrr+couponDiscountIrr>SubtotalAmountIRR)
            throw new DomainException("Invalid order discounts.");
        if(campaignDiscountIrr>0&&couponDiscountIrr>0) throw new DomainException("Campaign and coupon cannot be combined.");
        if(checked(SubtotalAmountIRR-campaignDiscountIrr-couponDiscountIrr+ShippingFeeIRR)!=TotalAmountIRR)
            throw new DomainException("Order total does not match discounts.");
        CampaignDiscountIRR=campaignDiscountIrr; CouponDiscountIRR=couponDiscountIrr; CouponCodeSnapshot=string.IsNullOrWhiteSpace(couponCode)?null:couponCode.Trim().ToUpperInvariant();
    }
    public void SetShippingDestination(long cityId,string cityName,string provinceName){if(cityId<=0||string.IsNullOrWhiteSpace(cityName)||string.IsNullOrWhiteSpace(provinceName))throw new DomainException("Invalid shipping destination.");DestinationCityId=cityId;DestinationCityNameSnapshot=cityName.Trim();DestinationProvinceNameSnapshot=provinceName.Trim();}
    public void SetSellerAmount(long amount){if(amount<0||amount>TotalAmountIRR)throw new DomainException("Invalid seller amount.");SellerAmountIRR=amount;}
    public void MarkPaid(DateTime? now=null){Require(OrderStatus.PendingPayment);PaidAtUtc=now??DateTime.UtcNow;Status=OrderStatus.Paid;}
    public void StartPreparing(){Require(OrderStatus.Paid);Status=OrderStatus.Preparing;}
    public void MarkReady(){if(Status is not (OrderStatus.Preparing or OrderStatus.Paid))throw new DomainException("Order is not ready to be delivered.");Status=OrderStatus.ReadyForDelivery;}
    public void SetDeliveryExpiry(DateTime expiresAtUtc){if(Status is not (OrderStatus.Paid or OrderStatus.Preparing or OrderStatus.ReadyForDelivery))throw new DomainException("Invalid delivery expiry.");if(expiresAtUtc<=DateTime.UtcNow)throw new DomainException("Expiry must be in the future.");DeliveryExpiresAtUtc=expiresAtUtc;}
    public void MarkDelivered(DateTime deliveredAtUtc,DateTime complaintExpiresAtUtc){if(Status is not (OrderStatus.ReadyForDelivery or OrderStatus.Preparing))throw new DomainException("Order cannot be delivered.");if(complaintExpiresAtUtc<=deliveredAtUtc)throw new DomainException("Complaint expiry must be after delivery.");DeliveredAtUtc=deliveredAtUtc;ComplaintExpiresAtUtc=complaintExpiresAtUtc;Status=OrderStatus.Delivered;}
    public void MarkDeliveryExpired(DateTime now){if(Status!=OrderStatus.ReadyForDelivery)throw new DomainException("Order is not awaiting delivery.");if(DeliveryExpiresAtUtc is null||now<DeliveryExpiresAtUtc.Value)throw new DomainException("Delivery has not expired.");Status=OrderStatus.DeliveryExpired;}
    public void RequestRefund(){if(Status is not (OrderStatus.DeliveryExpired or OrderStatus.Delivered))throw new DomainException("Refund cannot be requested for this order.");Status=OrderStatus.RefundRequested;}
    public void MarkRefunded(){Require(OrderStatus.RefundRequested);Status=OrderStatus.Refunded;}
    public void Complete(DateTime now){if(Status!=OrderStatus.Delivered||ComplaintExpiresAtUtc is null||now<ComplaintExpiresAtUtc.Value)throw new DomainException("Complaint period has not expired.");Status=OrderStatus.Completed;}
    public void Cancel(){if(Status is OrderStatus.Paid or OrderStatus.Delivered or OrderStatus.Completed or OrderStatus.Refunded)throw new DomainException("Order cannot be cancelled.");Status=OrderStatus.Cancelled;}
    private void Require(OrderStatus expected){if(Status!=expected)throw new DomainException($"Order must be in {expected} status.");}
}