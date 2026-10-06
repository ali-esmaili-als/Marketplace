using Marketplace.Domain.Common;

namespace Marketplace.Domain.Sellers;

public sealed class Seller : AggregateRoot<long>
{
    private Seller() { }

    public long UserId { get; private set; }
    public SellerStatus Status { get; private set; }
    public int CommissionRateBasisPoints { get; private set; }
    public long MinimumCommissionIRR { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ActivatedAtUtc { get; private set; }

    public static Seller Create(long id, long userId, int commissionRateBasisPoints = 0, long minimumCommissionIrr = 0)
    {
        if (id <= 0 || userId <= 0) throw new DomainException("Seller identifiers must be positive.");
        if (commissionRateBasisPoints is < 0 or > 10000) throw new DomainException("Commission rate must be between 0 and 10000 basis points.");
        if (minimumCommissionIrr < 0) throw new DomainException("Minimum commission cannot be negative.");

        return new Seller
        {
            Id = id,
            UserId = userId,
            Status = SellerStatus.Pending,
            CommissionRateBasisPoints = commissionRateBasisPoints,
            MinimumCommissionIRR = minimumCommissionIrr,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Activate()
    {
        if (Status == SellerStatus.Rejected) throw new DomainException("Rejected seller cannot be activated.");
        Status = SellerStatus.Active;
        ActivatedAtUtc ??= DateTime.UtcNow;
    }

    public void Suspend()
    {
        if (Status == SellerStatus.Rejected) throw new DomainException("Rejected seller cannot be suspended.");
        Status = SellerStatus.Suspended;
    }

    public void Reject()
    {
        if (Status == SellerStatus.Active) throw new DomainException("Active seller cannot be rejected.");
        Status = SellerStatus.Rejected;
    }

    public void ConfigureCommission(int rateBasisPoints, long minimumCommissionIrr)
    {
        if (rateBasisPoints is < 0 or > 10000) throw new DomainException("Commission rate must be between 0 and 10000 basis points.");
        if (minimumCommissionIrr < 0) throw new DomainException("Minimum commission cannot be negative.");
        CommissionRateBasisPoints = rateBasisPoints;
        MinimumCommissionIRR = minimumCommissionIrr;
    }
}
