using CartEntity = Marketplace.Domain.Cart.Cart;
using Marketplace.Domain.Cart;

namespace Marketplace.Application.Abstractions;

public interface ICartRepository
{
    Task<CartEntity?> GetByCustomerAsync(long customerId, CancellationToken cancellationToken = default);
    Task<List<CartItem>> GetItemsAsync(long cartId, CancellationToken cancellationToken = default);
    Task<CartItem?> GetItemAsync(long cartId, long variantId, long? warrantyId, CancellationToken cancellationToken = default);
    void Add(CartEntity cart);
    void AddItem(CartItem item);
    void RemoveItem(CartItem item);
}
