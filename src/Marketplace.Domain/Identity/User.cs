using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public sealed class User : AggregateRoot<long>
{
    private User() { }

    public string Mobile { get; private set; } = null!;
    public string? Email { get; private set; }
    public string PasswordHash { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public bool IsMobileVerified { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? LastLoginAtUtc { get; private set; }

    public static User Create(long id, string mobile, string passwordHash, string displayName)
    {
        if (id <= 0) throw new DomainException("User identifier must be positive.");
        if (string.IsNullOrWhiteSpace(mobile)) throw new DomainException("Mobile is required.");
        if (string.IsNullOrWhiteSpace(passwordHash)) throw new DomainException("Password hash is required.");
        if (string.IsNullOrWhiteSpace(displayName)) throw new DomainException("Display name is required.");

        return new User
        {
            Id = id,
            Mobile = mobile.Trim(),
            PasswordHash = passwordHash,
            DisplayName = displayName.Trim(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void UpdateProfile(string displayName, string? email)
    {
        if (string.IsNullOrWhiteSpace(displayName)) throw new DomainException("Display name is required.");
        if (displayName.Trim().Length > 120) throw new DomainException("Display name cannot exceed 120 characters.");
        if (!string.IsNullOrWhiteSpace(email) && email.Trim().Length > 254) throw new DomainException("Email cannot exceed 254 characters.");
        DisplayName = displayName.Trim();
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    }
    public void SetEmail(string? email) => Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
    public void SetMobileVerified(bool value = true) => IsMobileVerified = value;
    public void SetPasswordHash(string hash)
    {
        if (string.IsNullOrWhiteSpace(hash)) throw new DomainException("Password hash is required.");
        PasswordHash = hash;
    }
    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
    public void MarkLogin() => LastLoginAtUtc = DateTime.UtcNow;
}