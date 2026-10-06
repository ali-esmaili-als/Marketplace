using Marketplace.Domain.Common;

namespace Marketplace.Domain.Sellers;

public sealed class Store : AggregateRoot<long>
{
    private Store() { }

    public long SellerId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public StoreStatus Status { get; private set; }
    public int CommissionRateBasisPoints { get; private set; }
    public long MinimumCommissionIRR { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Store Create(long id, long sellerId, string name, string slug,
        int commissionRateBasisPoints = 0, long minimumCommissionIrr = 0)
    {
        if (id <= 0 || sellerId <= 0) throw new DomainException("Invalid store identifiers.");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
            throw new DomainException("Store name and slug are required.");
        if (commissionRateBasisPoints is < 0 or > 10000)
            throw new DomainException("Commission rate must be between 0 and 10000 basis points.");
        if (minimumCommissionIrr < 0) throw new DomainException("Minimum commission cannot be negative.");

        return new Store
        {
            Id = id,
            SellerId = sellerId,
            Name = name.Trim(),
            Slug = slug.Trim(),
            Status = StoreStatus.Draft,
            CommissionRateBasisPoints = commissionRateBasisPoints,
            MinimumCommissionIRR = minimumCommissionIrr,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Activate() => Status = StoreStatus.Active;
    public void Suspend() => Status = StoreStatus.Suspended;
    public void Close() => Status = StoreStatus.Closed;

    public void Rename(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
            throw new DomainException("Store name and slug are required.");
        Name = name.Trim();
        Slug = slug.Trim();
    }

    public void ConfigureCommission(int rateBasisPoints, long minimumCommissionIrr)
    {
        if (rateBasisPoints is < 0 or > 10000 || minimumCommissionIrr < 0)
            throw new DomainException("Invalid commission configuration.");
        CommissionRateBasisPoints = rateBasisPoints;
        MinimumCommissionIRR = minimumCommissionIrr;
    }
}
