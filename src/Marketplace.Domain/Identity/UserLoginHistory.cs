using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public enum LoginType : byte
{
    Password = 1,
    Otp = 2,
    RefreshToken = 3
}

public sealed class UserLoginHistory : Entity<long>
{
    private UserLoginHistory() { }

    public long UserId { get; private set; }
    public LoginType LoginType { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public bool Succeeded { get; private set; }

    public static UserLoginHistory Create(long id, long userId, LoginType loginType, bool succeeded, string? ipAddress, string? userAgent)
        => new()
        {
            Id = id, UserId = userId, LoginType = loginType, Succeeded = succeeded,
            IpAddress = ipAddress, UserAgent = userAgent, CreatedAtUtc = DateTime.UtcNow
        };
}
