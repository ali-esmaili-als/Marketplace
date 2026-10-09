using Marketplace.Domain.Common;
using Marketplace.Domain.Identity;
using Xunit;

namespace Marketplace.Domain.Tests;

public sealed class UserProfileTests
{
    [Fact]
    public void UpdateProfile_ChangesDisplayNameAndOptionalEmail()
    {
        var user = User.Create(1, "09120000000", "hashed-password", "Before");

        user.UpdateProfile("After Name", "after@example.com");

        Assert.Equal("After Name", user.DisplayName);
        Assert.Equal("after@example.com", user.Email);
    }

    [Fact]
    public void UpdateProfile_ClearsEmailWhenBlank()
    {
        var user = User.Create(1, "09120000000", "hashed-password", "Before");
        user.SetEmail("before@example.com");

        user.UpdateProfile("After", " ");

        Assert.Null(user.Email);
    }

    [Theory]
    [InlineData("", null)]
    [InlineData("   ", "valid@example.com")]
    public void UpdateProfile_RejectsMissingDisplayName(string displayName, string? email)
    {
        var user = User.Create(1, "09120000000", "hashed-password", "Before");

        Assert.Throws<DomainException>(() => user.UpdateProfile(displayName, email));
    }

    [Fact]
    public void UpdateProfile_RejectsEmailLongerThan254Characters()
    {
        var user = User.Create(1, "09120000000", "hashed-password", "Before");

        Assert.Throws<DomainException>(() => user.UpdateProfile("After", new string('a', 250) + "@x.com"));
    }
}
