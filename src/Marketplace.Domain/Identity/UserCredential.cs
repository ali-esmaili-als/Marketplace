using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public sealed class UserCredential : Entity<long>
{
    private UserCredential() { }

    public string PasswordHash { get; private set; } = null!;
    public DateTime PasswordChangedAtUtc { get; private set; }
    public int FailedLoginCount { get; private set; }
    public DateTime? LastFailedLoginAtUtc { get; private set; }

    public static UserCredential Create(long userId, string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException("Password hash is required.");
        return new UserCredential
        {
            Id = userId,
            PasswordHash = passwordHash,
            PasswordChangedAtUtc = DateTime.UtcNow
        };
    }

    public void ChangePassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException("Password hash is required.");
        PasswordHash = passwordHash;
        PasswordChangedAtUtc = DateTime.UtcNow;
        FailedLoginCount = 0;
        LastFailedLoginAtUtc = null;
    }

    public void RegisterFailedLogin()
    {
        FailedLoginCount++;
        LastFailedLoginAtUtc = DateTime.UtcNow;
    }

    public void ResetFailedLogins()
    {
        FailedLoginCount = 0;
        LastFailedLoginAtUtc = null;
    }
}
