using Marketplace.Application.Abstractions;
using Marketplace.Domain.Cart;
using Marketplace.Domain.Common;

namespace Marketplace.Application.Cart;

public sealed class CartService
{
    private readonly ICartRepository _carts;
    private readonly ICatalogRepository _catalog;
    private readonly IUnitOfWork _uow;
    private readonly IIdGenerator _ids;

    public CartService(ICartRepository carts, ICatalogRepository catalog, IUnitOfWork uow, IIdGenerator ids)
    {
        _carts=carts; _catalog=catalog; _uow=uow; _ids=ids;
    }

    public Task AddItemAsync(long customerId, long sellerId, long storeId, long productId, long variantId, int quantity, long? warrantyId, CancellationToken ct=default)
        => _uow.ExecuteInTransactionAsync(async token =>
        {
            var line = await _catalog.GetCheckoutLineAsync(variantId, warrantyId, token)
                ?? throw new DomainException("Product variant or warranty not found.");
            if (line.Product.Id != productId) throw new DomainException("Variant does not belong to the product.");
            if (line.Product.StoreId != storeId) throw new DomainException("Product does not belong to the selected store.");
            if (!line.Product.Status.Equals(Marketplace.Domain.Catalog.ProductStatus.Active) || !line.Variant.IsActive)
                throw new DomainException("Product is not available.");
            if (line.Warranty is not null && !line.Warranty.IsActive) throw new DomainException("Warranty is not available.");

            var cart = await _carts.GetByCustomerAsync(customerId, token);
            if (cart is null)
            {
                cart = Cart.Create(await _ids.NextAsync(token), customerId, sellerId, storeId);
                _carts.Add(cart);
            }
            else if (cart.SellerId != sellerId || cart.StoreId != storeId)
                throw new DomainException("Cart can contain items from only one seller store.");

            var item = await _carts.GetItemAsync(cart.Id, variantId, warrantyId, token);
            if (item is null) _carts.AddItem(CartItem.Create(await _ids.NextAsync(token), cart.Id, productId, variantId, quantity, warrantyId));
            else item.ChangeQuantity(checked(item.Quantity + quantity));
            cart.Touch();
            await _uow.SaveChangesAsync(token);
            return 0;
        }, ct);

    public Task RemoveItemAsync(long customerId, long variantId, long? warrantyId, CancellationToken ct=default)
        => _uow.ExecuteInTransactionAsync(async token =>
        {
            var cart=await _carts.GetByCustomerAsync(customerId,token)??throw new DomainException("Cart not found.");
            var item=await _carts.GetItemAsync(cart.Id,variantId,warrantyId,token)??throw new DomainException("Cart item not found.");
            _carts.RemoveItem(item); cart.Touch(); await _uow.SaveChangesAsync(token); return 0;
        }, ct);

    public async Task<List<CartItem>> GetItemsAsync(long customerId, CancellationToken ct=default)
    {
        var cart=await _carts.GetByCustomerAsync(customerId,ct);
        return cart is null ? new List<CartItem>() : await _carts.GetItemsAsync(cart.Id,ct);
    }
}
