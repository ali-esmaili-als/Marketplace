using Marketplace.Domain.Common;

namespace Marketplace.Domain.Sellers;

public sealed class Seller : AggregateRoot<long>
{
    private Seller() { }

    public long UserId { get; private set; }
    public byte SellerPlanId { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Seller Create(long id, long userId, byte sellerPlanId)
    {
        return new Seller
        {
            Id = id,
            UserId = userId,
            SellerPlanId = sellerPlanId,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };
    }

    public void ChangePlan(byte sellerPlanId)
    {
        SellerPlanId = sellerPlanId;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void Deactivate() { IsActive = false; UpdatedAtUtc = DateTime.UtcNow; }
    public void Activate() { IsActive = true; UpdatedAtUtc = DateTime.UtcNow; }
}
