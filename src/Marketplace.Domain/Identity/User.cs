using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public sealed class User : AggregateRoot<long>
{
    private readonly List<UserTypeId> _userTypes = [];

    private User() { }

    private User(long id, string firstName, string lastName) : base(id)
    {
        FirstName = firstName;
        LastName = lastName;
        CreatedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = CreatedAtUtc;
        IsActive = true;
    }

    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public string? Mobile { get; private set; }
    public string? Email { get; private set; }
    public bool IsMobileVerified { get; private set; }
    public bool IsEmailVerified { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsLocked { get; private set; }
    public DateTime? LockoutEndUtc { get; private set; }
    public DateTime? LastLoginAtUtc { get; private set; }
    public string SecurityStamp { get; private set; } = Guid.NewGuid().ToString("N");
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public IReadOnlyCollection<UserTypeId> UserTypes => _userTypes.AsReadOnly();

    public static User Create(long id, string firstName, string lastName)
    {
        if (string.IsNullOrWhiteSpace(firstName)) throw new DomainException("First name is required.");
        if (string.IsNullOrWhiteSpace(lastName)) throw new DomainException("Last name is required.");
        return new User(id, firstName.Trim(), lastName.Trim());
    }

    public void AddUserType(UserTypeId userType)
    {
        if (!_userTypes.Contains(userType)) _userTypes.Add(userType);
        Touch();
    }

    public void RemoveUserType(UserTypeId userType)
    {
        _userTypes.Remove(userType);
        Touch();
    }

    public void SetMobile(string? mobile)
    {
        Mobile = string.IsNullOrWhiteSpace(mobile) ? null : mobile.Trim();
        IsMobileVerified = false;
        Touch();
    }

    public void SetEmail(string? email)
    {
        Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        IsEmailVerified = false;
        Touch();
    }

    public void MarkMobileVerified() { IsMobileVerified = true; Touch(); }
    public void MarkEmailVerified() { IsEmailVerified = true; Touch(); }
    public void RecordLogin() { LastLoginAtUtc = DateTime.UtcNow; Touch(); }
    public void LockUntil(DateTime? untilUtc) { IsLocked = true; LockoutEndUtc = untilUtc; Touch(); }
    public void Unlock() { IsLocked = false; LockoutEndUtc = null; Touch(); }
    public void Deactivate() { IsActive = false; Touch(); }
    public void Activate() { IsActive = true; Touch(); }

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
