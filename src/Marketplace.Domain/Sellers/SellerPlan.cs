using Marketplace.Domain.Common;

namespace Marketplace.Domain.Sellers;

public sealed class SellerPlan : Entity<byte>
{
    private SellerPlan() { }

    public string Name { get; private set; } = null!;
    public int MaxStores { get; private set; }
    public bool IsActive { get; private set; }

    public static SellerPlan Create(byte id, string name, int maxStores)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Seller plan name is required.");
        if (maxStores <= 0) throw new DomainException("Maximum stores must be greater than zero.");
        return new SellerPlan { Id = id, Name = name.Trim(), MaxStores = maxStores, IsActive = true };
    }
}
