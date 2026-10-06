using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public sealed class UserSession : Entity<long>
{
    private UserSession() { }

    public long UserId { get; private set; }
    public byte[] SessionKeyHash { get; private set; } = null!;
    public string? Device { get; private set; }
    public string? UserAgent { get; private set; }
    public string? IpAddress { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }

    public static UserSession Create(long id, long userId, byte[] sessionKeyHash, string? device, string? userAgent, string? ipAddress)
    {
        if (sessionKeyHash.Length == 0) throw new DomainException("Session key hash is required.");
        var now = DateTime.UtcNow;
        return new UserSession
        {
            Id = id, UserId = userId, SessionKeyHash = sessionKeyHash, Device = device,
            UserAgent = userAgent, IpAddress = ipAddress, CreatedAtUtc = now, LastActivityAtUtc = now
        };
    }

    public void Touch() => LastActivityAtUtc = DateTime.UtcNow;
    public void Revoke() => RevokedAtUtc ??= DateTime.UtcNow;
}
