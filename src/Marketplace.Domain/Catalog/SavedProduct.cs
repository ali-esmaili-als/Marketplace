namespace Marketplace.Domain.Catalog;

/// <summary>
/// A customer-owned saved product. The row stores the customer identity and product identity only;
/// current price and availability are always read from the catalog.
/// </summary>
public sealed class SavedProduct
{
    private SavedProduct() { }

    public long Id { get; private set; }
    public long CustomerId { get; private set; }
    public long ProductId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static SavedProduct Create(long id, long customerId, long productId, DateTime? createdAtUtc = null)
    {
        if (id <= 0 || customerId <= 0 || productId <= 0)
            throw new Marketplace.Domain.Common.DomainException("Invalid saved product.");

        return new SavedProduct
        {
            Id = id,
            CustomerId = customerId,
            ProductId = productId,
            CreatedAtUtc = createdAtUtc ?? DateTime.UtcNow
        };
    }
}
