using Marketplace.Application.Abstractions;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Pricing;
using Marketplace.Domain.Sellers;
using Marketplace.Application.Pricing;

namespace Marketplace.Application.Orders;

public sealed record CheckoutResult(long OrderId,long PaymentId,string Provider,string Authority,string RedirectUrl,long TotalAmountIRR,long SubtotalAmountIRR,long CampaignDiscountIRR,long CouponDiscountIRR,string? CouponCode);

public sealed class OrderCreationService
{
    private readonly ICartRepository _carts; private readonly ICatalogRepository _catalog; private readonly IOrderRepository _orders;
    private readonly IPaymentRepository _payments; private readonly ILifecycleRepository _life; private readonly IUnitOfWork _uow;
    private readonly IIdGenerator _ids; private readonly IPaymentGatewayFactory _gatewayFactory; private readonly IShippingRepository _shipping;
    private readonly PricingService _pricing;

    public OrderCreationService(ICartRepository carts,ICatalogRepository catalog,IOrderRepository orders,IPaymentRepository payments,ILifecycleRepository life,IUnitOfWork uow,IIdGenerator ids,IPaymentGatewayFactory gatewayFactory,IShippingRepository shipping,PricingService pricing)
    { _carts=carts;_catalog=catalog;_orders=orders;_payments=payments;_life=life;_uow=uow;_ids=ids;_gatewayFactory=gatewayFactory;_shipping=shipping;_pricing=pricing; }

    public async Task<CheckoutResult> CheckoutAsync(long customerId,PaymentProviderCode provider,long destinationCityId,string? couponCode,CancellationToken ct=default)
    {
        long orderId=0,paymentId=0,total=0,subtotal=0,campaignDiscount=0,couponDiscount=0; string? appliedCoupon=null;

        await _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            var cart=await _carts.GetByCustomerAsync(customerId,token)??throw new DomainException("Cart is empty.");
            var items=await _carts.GetItemsAsync(cart.Id,token);
            if(items.Count==0) throw new DomainException("Cart is empty.");
            var store=await _catalog.GetStoreAsync(cart.StoreId,token)??throw new DomainException("Store not found.");
            if(store.SellerId!=cart.SellerId||store.Status!=StoreStatus.Active) throw new DomainException("Store is not available.");

            var city=await _shipping.GetCityAsync(destinationCityId,token)??throw new DomainException("Destination city was not found.");
            if(!city.IsActive) throw new DomainException("Destination city is not active.");
            if(!await _shipping.StoreShipsToCityAsync(store.Id,city.Id,token)) throw new DomainException($"This store does not ship to {city.Name}.");

            var input=new List<(CartItem,CheckoutLineData)>(items.Count);
            foreach(var item in items)
            {
                var data=await _catalog.GetCheckoutLineAsync(item.ProductVariantId,item.WarrantyId,token)??throw new DomainException("A cart product is no longer available.");
                if(data.Product.StoreId!=cart.StoreId||data.Product.Id!=item.ProductId) throw new DomainException("Cart item is invalid.");
                if(data.Product.Status!=ProductStatus.Active||!data.Variant.IsActive) throw new DomainException($"Product {data.Product.Name} is no longer available.");
                if(item.Quantity>data.Inventory.AvailableQuantity) throw new DomainException($"Insufficient stock for {data.Product.Name}.");
                if(item.WarrantyId.HasValue&&(data.Warranty is null||!data.Warranty.IsActive)) throw new DomainException($"Warranty is no longer available for {data.Product.Name}.");
                input.Add((item,data));
            }

            var priced=await _pricing.PriceAsync(customerId,store.Id,store.SellerId,input,couponCode,DateTime.UtcNow,token);
            subtotal=priced.SubtotalIRR;campaignDiscount=priced.CampaignDiscountIRR;couponDiscount=priced.CouponDiscountIRR;total=priced.TotalIRR;appliedCoupon=priced.Coupon?.Code;
            if(total<=0) throw new DomainException("Order total must be positive.");

            orderId=await _ids.NextAsync(token); paymentId=await _ids.NextAsync(token);
            var commissionRate=store.CommissionRateBasisPoints/100m;
            var commission=Commission.Create(await _ids.NextAsync(token),orderId,store.Id,store.SellerId,total,commissionRate,store.MinimumCommissionIRR);
            var order=Order.Create(orderId,customerId,store.SellerId,store.Id,subtotal,total);
            order.SetDiscounts(campaignDiscount,couponDiscount,appliedCoupon);
            order.SetShippingDestination(city.Id,city.Name,city.ProvinceName);
            order.SetSellerAmount(commission.SellerAmountIRR);
            _orders.Add(order); _life.AddCommission(commission);

            foreach(var p in priced.Lines)
            {
                var variantSnapshot=$"{p.Data.Variant.SKU} | {p.Data.Variant.VariantKey}";
                var item=OrderItem.Create(await _ids.NextAsync(token),orderId,p.Data.Product.Id,p.Data.Variant.Id,p.Data.Product.Name,variantSnapshot,p.BaseUnitIRR,p.Item.Quantity,p.Data.Warranty?.Id??0,p.Data.Warranty?.Name,p.WarrantyIRR,p.CampaignDiscountIRR,p.CouponDiscountIRR,p.Campaign?.Id,p.Campaign?.Name);
                _life.AddOrderItem(item);
                p.Data.Inventory.Reserve(p.Item.Quantity);
                _life.AddInventoryReservation(InventoryReservation.Create(await _ids.NextAsync(token),p.Data.Variant.Id,orderId,p.Item.Quantity,DateTime.UtcNow.AddHours(24)));
            }

            if(priced.Coupon is not null)
                _life.AddCouponUsage(CouponUsage.Create(await _ids.NextAsync(token),priced.Coupon.Id,customerId,orderId,couponDiscount));

            _payments.Add(Payment.Create(paymentId,orderId,customerId,total));
            foreach(var item in items) _carts.RemoveItem(item);
            await _uow.SaveChangesAsync(token); return 0;
        },ct);

        var gateway=await _gatewayFactory.GetAsync(provider,ct);
        var redirect=await gateway.CreatePaymentAsync(paymentId,orderId,total,ct);
        await _uow.ExecuteInTransactionAsync(async token =>
        {
            var payment=await _payments.GetAsync(paymentId,token)??throw new DomainException("Payment not found.");
            payment.Redirect(redirect.Provider,redirect.Authority);
            _payments.AddTransaction(PaymentTransaction.Create(await _ids.NextAsync(token),payment.Id,payment.AmountIRR,redirect.Provider,redirect.Authority));
            await _uow.SaveChangesAsync(token); return 0;
        },ct);
        return new CheckoutResult(orderId,paymentId,redirect.Provider,redirect.Authority,redirect.Url,total,subtotal,campaignDiscount,couponDiscount,appliedCoupon);
    }
}