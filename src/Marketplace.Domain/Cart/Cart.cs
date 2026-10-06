using Marketplace.Domain.Common;

namespace Marketplace.Domain.Cart;

public sealed class Cart : AggregateRoot<long>
{
    private Cart() { }

    public long CustomerId { get; private set; }
    public long StoreId { get; private set; }
    public long SellerId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Cart Create(long id, long customerId, long sellerId, long storeId)
    {
        if (id <= 0 || customerId <= 0 || sellerId <= 0 || storeId <= 0)
            throw new DomainException("Invalid cart.");
        var now = DateTime.UtcNow;
        return new Cart { Id=id, CustomerId=customerId, SellerId=sellerId, StoreId=storeId, CreatedAtUtc=now, UpdatedAtUtc=now };
    }

    public void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
