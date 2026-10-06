using Marketplace.Application.Abstractions;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Catalog;
using Marketplace.Domain.Common;
using Marketplace.Domain.Finance;
using Marketplace.Domain.Inventory;
using Marketplace.Domain.Orders;
using Marketplace.Domain.Payments;
using Marketplace.Domain.Sellers;

namespace Marketplace.Application.Orders;

public sealed record CheckoutResult(long OrderId, long PaymentId, string Provider, string Authority, string RedirectUrl, long TotalAmountIRR);

public sealed class OrderCreationService
{
    private readonly ICartRepository _carts;
    private readonly ICatalogRepository _catalog;
    private readonly IOrderRepository _orders;
    private readonly IPaymentRepository _payments;
    private readonly ILifecycleRepository _life;
    private readonly IUnitOfWork _uow;
    private readonly IIdGenerator _ids;
    private readonly IPaymentGatewayFactory _gatewayFactory;
    private readonly IShippingRepository _shipping;

    public OrderCreationService(
        ICartRepository carts, ICatalogRepository catalog, IOrderRepository orders,
        IPaymentRepository payments, ILifecycleRepository life, IUnitOfWork uow,
        IIdGenerator ids, IPaymentGatewayFactory gatewayFactory, IShippingRepository shipping)
    {
        _carts=carts; _catalog=catalog; _orders=orders; _payments=payments;
        _life=life; _uow=uow; _ids=ids; _gatewayFactory=gatewayFactory; _shipping=shipping;
    }

    public async Task<CheckoutResult> CheckoutAsync(long customerId,Marketplace.Domain.Payments.PaymentProviderCode provider,long destinationCityId,CancellationToken ct=default)
    {
        long orderId=0, paymentId=0, total=0;

        await _uow.ExecuteInSerializableTransactionAsync(async token =>
        {
            var cart=await _carts.GetByCustomerAsync(customerId,token)??throw new DomainException("Cart is empty.");
            var items=await _carts.GetItemsAsync(cart.Id,token);
            if(items.Count==0) throw new DomainException("Cart is empty.");

            var store=await _catalog.GetStoreAsync(cart.StoreId,token)??throw new DomainException("Store not found.");
            if(store.SellerId!=cart.SellerId || store.Status!=StoreStatus.Active)
                throw new DomainException("Store is not available.");

            var destinationCity = await _shipping.GetCityAsync(destinationCityId, token)
                ?? throw new DomainException("Destination city was not found.");
            if (!destinationCity.IsActive)
                throw new DomainException("Destination city is not active.");
            if (!await _shipping.StoreShipsToCityAsync(store.Id, destinationCity.Id, token))
                throw new DomainException($"This store does not ship to {destinationCity.Name}.");

            var priced = new List<(CartItem Item, CheckoutLineData Data, long Unit, long Warranty, long Line)>();
            foreach(var item in items)
            {
                var data=await _catalog.GetCheckoutLineAsync(item.ProductVariantId,item.WarrantyId,token)
                    ??throw new DomainException("A cart product is no longer available.");

                if(data.Product.StoreId!=cart.StoreId || data.Product.Id!=item.ProductId)
                    throw new DomainException("Cart item is invalid.");
                if(data.Product.Status!=ProductStatus.Active || !data.Variant.IsActive)
                    throw new DomainException($"Product {data.Product.Name} is no longer available.");
                if(item.Quantity>data.Inventory.AvailableQuantity)
                    throw new DomainException($"Insufficient stock for {data.Product.Name}.");
                if(item.WarrantyId.HasValue && (data.Warranty is null || !data.Warranty.IsActive))
                    throw new DomainException($"Warranty is no longer available for {data.Product.Name}.");

                var unit=data.Variant.PriceIRR ?? data.Product.BasePriceIRR;
                var warranty=data.Warranty?.PriceIRR ?? 0;
                var line=checked((unit+warranty)*item.Quantity);
                priced.Add((item,data,unit,warranty,line));
                total=checked(total+line);
            }

            if(total<=0) throw new DomainException("Order total must be positive.");

            orderId=await _ids.NextAsync(token);
            paymentId=await _ids.NextAsync(token);

            var commissionRate=store.CommissionRateBasisPoints/100m;
            var commission=Commission.Create(
                await _ids.NextAsync(token), orderId, store.Id, store.SellerId,
                total, commissionRate, store.MinimumCommissionIRR);

            var order=Order.Create(orderId,customerId,store.SellerId,store.Id,total);
            order.SetShippingDestination(destinationCity.Id,destinationCity.Name,destinationCity.ProvinceName);
            order.SetSellerAmount(commission.SellerAmountIRR);
            _orders.Add(order);
            _life.AddCommission(commission);

            foreach(var p in priced)
            {
                var variantSnapshot=$"{p.Data.Variant.SKU} | {p.Data.Variant.VariantKey}";
                var orderItem=OrderItem.Create(
                    await _ids.NextAsync(token),orderId,p.Data.Product.Id,p.Data.Variant.Id,
                    p.Data.Product.Name,variantSnapshot,p.Unit,p.Item.Quantity,
                    p.Data.Warranty?.Id??0,p.Data.Warranty?.Name,p.Warranty);
                _life.AddOrderItem(orderItem);

                p.Data.Inventory.Reserve(p.Item.Quantity);
                var reservation=InventoryReservation.Create(
                    await _ids.NextAsync(token),p.Data.Variant.Id,orderId,p.Item.Quantity,
                    DateTime.UtcNow.AddHours(24));
                _life.AddInventoryReservation(reservation);
            }

            var payment=Payment.Create(paymentId,orderId,customerId,total);
            _payments.Add(payment);

            // The cart is consumed only after the order, inventory reservations and payment
            // snapshot are all persisted in the same DB transaction.
            foreach(var item in items) _carts.RemoveItem(item);

            await _uow.SaveChangesAsync(token);
            return 0;
        },ct);

        // Never hold a SQL transaction open while calling the external payment gateway.
        var gateway=await _gatewayFactory.GetAsync(provider,ct);
        var redirect=await gateway.CreatePaymentAsync(paymentId,orderId,total,ct);

        await _uow.ExecuteInTransactionAsync(async token =>
        {
            var payment=await _payments.GetAsync(paymentId,token)??throw new DomainException("Payment not found.");
            payment.Redirect(redirect.Provider,redirect.Authority);
            var transaction=PaymentTransaction.Create(await _ids.NextAsync(token),payment.Id,payment.AmountIRR,redirect.Provider,redirect.Authority);
            _payments.AddTransaction(transaction);
            await _uow.SaveChangesAsync(token);
            return 0;
        },ct);

        return new CheckoutResult(orderId,paymentId,redirect.Provider,redirect.Authority,redirect.Url,total);
    }
}
