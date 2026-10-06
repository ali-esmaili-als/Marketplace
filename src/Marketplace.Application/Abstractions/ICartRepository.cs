using Marketplace.Domain.Cart;

namespace Marketplace.Application.Abstractions;

public interface ICartRepository
{
    Task<Cart?> GetByCustomerAsync(long customerId, CancellationToken cancellationToken = default);
    Task<List<CartItem>> GetItemsAsync(long cartId, CancellationToken cancellationToken = default);
    Task<CartItem?> GetItemAsync(long cartId, long variantId, long? warrantyId, CancellationToken cancellationToken = default);
    void Add(Cart cart);
    void AddItem(CartItem item);
    void RemoveItem(CartItem item);
}
