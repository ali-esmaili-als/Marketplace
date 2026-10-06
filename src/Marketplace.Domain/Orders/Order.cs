using Marketplace.Domain.Common;

namespace Marketplace.Domain.Orders;

public sealed class Order : AggregateRoot<long>
{
    private readonly List<OrderItem> _items=[];
    private Order() { }
    public string OrderNumber { get; private set; } = null!;
    public long CustomerId { get; private set; }
    public long StoreId { get; private set; }
    public long? CartId { get; private set; }
    public OrderStatus Status { get; private set; }
    public long SubTotalIRR { get; private set; }
    public long CampaignDiscountIRR { get; private set; }
    public long DirectDiscountIRR { get; private set; }
    public long CouponDiscountIRR { get; private set; }
    public long WarrantyAmountIRR { get; private set; }
    public long ShippingGrossIRR { get; private set; }
    public long ShippingBenefitIRR { get; private set; }
    public long ShippingAmountIRR { get; private set; }
    public long TotalAmountIRR { get; private set; }
    public long? CouponId { get; private set; }
    public string? CouponCodeSnapshot { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public IReadOnlyCollection<OrderItem> Items=>_items.AsReadOnly();

    public static Order Create(long id,string orderNumber,long customerId,long storeId,long? cartId,long subtotal,long campaignDiscount,long directDiscount,long couponDiscount,long warrantyAmount,long shippingGross,long shippingBenefit,long shippingAmount,long? couponId,string? couponCode)
    {
        if(string.IsNullOrWhiteSpace(orderNumber)) throw new DomainException("Order number is required.");
        if(new[]{subtotal,campaignDiscount,directDiscount,couponDiscount,warrantyAmount,shippingGross,shippingBenefit,shippingAmount}.Any(x=>x<0)) throw new DomainException("Order amounts cannot be negative.");
        if(shippingBenefit>shippingGross) throw new DomainException("Shipping benefit cannot exceed gross shipping.");
        var total=checked(subtotal-campaignDiscount-directDiscount-couponDiscount+warrantyAmount+shippingAmount);
        if(total<0) throw new DomainException("Order total cannot be negative.");
        var now=DateTime.UtcNow;
        return new Order { Id=id, OrderNumber=orderNumber.Trim(), CustomerId=customerId, StoreId=storeId, CartId=cartId, Status=OrderStatus.PendingPayment, SubTotalIRR=subtotal, CampaignDiscountIRR=campaignDiscount, DirectDiscountIRR=directDiscount, CouponDiscountIRR=couponDiscount, WarrantyAmountIRR=warrantyAmount, ShippingGrossIRR=shippingGross, ShippingBenefitIRR=shippingBenefit, ShippingAmountIRR=shippingAmount, TotalAmountIRR=total, CouponId=couponId, CouponCodeSnapshot=couponCode, CreatedAtUtc=now, UpdatedAtUtc=now };
    }
    public void AddItem(OrderItem item){_items.Add(item);Touch();}
    public void MarkPaid(){Require(OrderStatus.PendingPayment);Status=OrderStatus.Paid;Touch();}
    public void StartPreparing(){Require(OrderStatus.Paid);Status=OrderStatus.Preparing;Touch();}
    public void MarkReadyForDelivery(){Require(OrderStatus.Preparing);Status=OrderStatus.ReadyForDelivery;Touch();}
    public void MarkDelivered(){Require(OrderStatus.ReadyForDelivery);Status=OrderStatus.Delivered;Touch();}
    public void Complete(){Require(OrderStatus.Delivered);Status=OrderStatus.Completed;Touch();}
    public void Cancel(){if(Status is OrderStatus.Paid or OrderStatus.Preparing or OrderStatus.ReadyForDelivery or OrderStatus.Delivered or OrderStatus.Completed) throw new DomainException("Order cannot be cancelled in its current status.");Status=OrderStatus.Cancelled;Touch();}
    public void MarkDeliveryExpired(){Require(OrderStatus.Paid);Status=OrderStatus.DeliveryExpired;Touch();}
    public void OpenDispute(){if(Status is not (OrderStatus.Paid or OrderStatus.Preparing or OrderStatus.ReadyForDelivery or OrderStatus.Delivered)) throw new DomainException("Order cannot be disputed in its current status.");Status=OrderStatus.Disputed;Touch();}
    public void RequestRefund(){if(Status is not (OrderStatus.Delivered or OrderStatus.Completed or OrderStatus.Disputed)) throw new DomainException("Refund cannot be requested in this status.");Status=OrderStatus.RefundRequested;Touch();}
    public void MarkRefunded(){Status=OrderStatus.Refunded;Touch();}
    public void MarkPartiallyRefunded(){Status=OrderStatus.PartiallyRefunded;Touch();}
    private void Require(OrderStatus status){if(Status!=status)throw new DomainException($"Order must be in {status} status.");}
    private void Touch()=>UpdatedAtUtc=DateTime.UtcNow;
}
