using Marketplace.Domain.Common;

namespace Marketplace.Domain.Sellers;

public sealed class Store : AggregateRoot<long>
{
    private Store() { }

    public long SellerId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string? Description { get; private set; }
    public string? LogoUrl { get; private set; }
    public string? BannerUrl { get; private set; }
    public decimal CommissionRate { get; private set; }
    public long MinCommissionIRR { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Store Create(long id, long sellerId, string name, string slug, decimal commissionRate, long minCommissionIrr)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Store name is required.");
        if (string.IsNullOrWhiteSpace(slug)) throw new DomainException("Store slug is required.");
        if (commissionRate < 0 || commissionRate > 100) throw new DomainException("Commission rate must be between 0 and 100.");
        if (minCommissionIrr < 0) throw new DomainException("Minimum commission cannot be negative.");
        var now = DateTime.UtcNow;
        return new Store
        {
            Id = id, SellerId = sellerId, Name = name.Trim(), Slug = slug.Trim().ToLowerInvariant(),
            CommissionRate = commissionRate, MinCommissionIRR = minCommissionIrr,
            IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
        };
    }

    public void Update(string name, string slug, string? description, string? logoUrl, string? bannerUrl)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug)) throw new DomainException("Store name and slug are required.");
        Name = name.Trim();
        Slug = slug.Trim().ToLowerInvariant();
        Description = description;
        LogoUrl = logoUrl;
        BannerUrl = bannerUrl;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void ChangeCommission(decimal rate, long minimumCommissionIrr)
    {
        if (rate < 0 || rate > 100) throw new DomainException("Commission rate must be between 0 and 100.");
        if (minimumCommissionIrr < 0) throw new DomainException("Minimum commission cannot be negative.");
        CommissionRate = rate;
        MinCommissionIRR = minimumCommissionIrr;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate() { IsActive = false; UpdatedAtUtc = DateTime.UtcNow; }
    public void Activate() { IsActive = true; UpdatedAtUtc = DateTime.UtcNow; }
}
