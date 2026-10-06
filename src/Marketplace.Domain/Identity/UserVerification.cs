using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public enum VerificationType : byte
{
    MobileVerification = 1,
    EmailVerification = 2,
    LoginOtp = 3,
    PasswordReset = 4
}

public sealed class UserVerification : Entity<long>
{
    private UserVerification() { }

    public long UserId { get; private set; }
    public VerificationType Type { get; private set; }
    public byte[] CodeHash { get; private set; } = null!;
    public DateTime ExpiresAtUtc { get; private set; }
    public int Attempts { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static UserVerification Create(long id, long userId, VerificationType type, byte[] codeHash, DateTime expiresAtUtc)
    {
        if (codeHash.Length == 0) throw new DomainException("Verification code hash is required.");
        return new UserVerification
        {
            Id = id, UserId = userId, Type = type, CodeHash = codeHash,
            ExpiresAtUtc = expiresAtUtc, CreatedAtUtc = DateTime.UtcNow
        };
    }

    public bool IsUsable(DateTime utcNow) => UsedAtUtc is null && ExpiresAtUtc > utcNow;
    public void RegisterAttempt() => Attempts++;
    public void MarkUsed() => UsedAtUtc ??= DateTime.UtcNow;
}
