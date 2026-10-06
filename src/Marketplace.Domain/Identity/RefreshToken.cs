using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public sealed class RefreshToken : Entity<long>
{
    private RefreshToken() { }

    public long UserId { get; private set; }
    public byte[] TokenHash { get; private set; } = null!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public long? ReplacedByTokenId { get; private set; }
    public string? CreatedByIp { get; private set; }
    public string? RevokedByIp { get; private set; }

    public static RefreshToken Create(long id, long userId, byte[] tokenHash, DateTime expiresAtUtc, string? createdByIp)
    {
        if (tokenHash.Length == 0) throw new DomainException("Token hash is required.");
        return new RefreshToken
        {
            Id = id, UserId = userId, TokenHash = tokenHash, ExpiresAtUtc = expiresAtUtc,
            CreatedAtUtc = DateTime.UtcNow, CreatedByIp = createdByIp
        };
    }

    public bool IsActive(DateTime utcNow) => RevokedAtUtc is null && ExpiresAtUtc > utcNow;

    public void Revoke(string? revokedByIp, long? replacedByTokenId = null)
    {
        if (RevokedAtUtc is not null) return;
        RevokedAtUtc = DateTime.UtcNow;
        RevokedByIp = revokedByIp;
        ReplacedByTokenId = replacedByTokenId;
    }
}
