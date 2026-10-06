using Marketplace.Domain.Authorization;
using Marketplace.Domain.Identity;
using Marketplace.Infrastructure.Authorization;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Tests.Authorization;

public sealed class PermissionCheckerTests
{
    [Fact]
    public async Task UserType_grant_allows_permission()
    {
        await using var db = CreateDb();
        var user = User.Create(1, "Ali", "Test");
        user.AddUserType(UserTypeId.Customer);

        db.Users.Add(user);
        db.Permissions.Add(Permission.Create(100, "Checkout.Pay", "Checkout / Pay"));
        db.PermissionUserTypes.Add(
            PermissionUserType.Create(1000, 100, UserTypeId.Customer));
        await db.SaveChangesAsync();

        var sut = new EfPermissionChecker(db);

        Assert.True(await sut.HasPermissionAsync(1, "Checkout.Pay"));
    }

    [Fact]
    public async Task User_rule_grant_allows_permission()
    {
        await using var db = CreateDb();
        var user = User.Create(1, "Ali", "Test");
        db.Users.Add(user);

        db.Permissions.Add(Permission.Create(100, "Settlement.Request", "Settlement / Request"));
        db.Rules.Add(Rule.Create(200, "settlement-basic", "Settlement Basic", 1));
        db.RulePermissions.Add(RulePermission.Create(300, 200, 100));
        db.UserRules.Add(UserRule.Create(400, 1, 200));
        await db.SaveChangesAsync();

        var sut = new EfPermissionChecker(db);

        Assert.True(await sut.HasPermissionAsync(1, "Settlement.Request"));
    }

    [Fact]
    public async Task Disabled_rule_does_not_grant_permission()
    {
        await using var db = CreateDb();
        var user = User.Create(1, "Ali", "Test");
        db.Users.Add(user);

        var rule = Rule.Create(200, "settlement-basic", "Settlement Basic", 1);
        rule.Disable();

        db.Permissions.Add(Permission.Create(100, "Settlement.Request", "Settlement / Request"));
        db.Rules.Add(rule);
        db.RulePermissions.Add(RulePermission.Create(300, 200, 100));
        db.UserRules.Add(UserRule.Create(400, 1, 200));
        await db.SaveChangesAsync();

        var sut = new EfPermissionChecker(db);

        Assert.False(await sut.HasPermissionAsync(1, "Settlement.Request"));
    }

    [Fact]
    public async Task Disabled_permission_does_not_grant_permission()
    {
        await using var db = CreateDb();
        var user = User.Create(1, "Ali", "Test");

        var permission = Permission.Create(100, "Settlement.Request", "Settlement / Request");
        permission.Disable();

        db.Users.Add(user);
        db.Permissions.Add(permission);
        db.PermissionUserTypes.Add(
            PermissionUserType.Create(1000, 100, UserTypeId.Customer));
        await db.SaveChangesAsync();

        var sut = new EfPermissionChecker(db);

        Assert.False(await sut.HasPermissionAsync(1, "Settlement.Request"));
    }

    [Fact]
    public async Task Disabled_user_rule_does_not_grant_permission()
    {
        await using var db = CreateDb();
        var user = User.Create(1, "Ali", "Test");
        var assignment = UserRule.Create(400, 1, 200);
        assignment.Disable();

        db.Users.Add(user);
        db.Permissions.Add(Permission.Create(100, "Settlement.Request", "Settlement / Request"));
        db.Rules.Add(Rule.Create(200, "settlement-basic", "Settlement Basic", 1));
        db.RulePermissions.Add(RulePermission.Create(300, 200, 100));
        db.UserRules.Add(assignment);
        await db.SaveChangesAsync();

        var sut = new EfPermissionChecker(db);

        Assert.False(await sut.HasPermissionAsync(1, "Settlement.Request"));
    }

    [Fact]
    public async Task Unknown_permission_is_denied()
    {
        await using var db = CreateDb();
        db.Users.Add(User.Create(1, "Ali", "Test"));
        await db.SaveChangesAsync();

        var sut = new EfPermissionChecker(db);

        Assert.False(await sut.HasPermissionAsync(1, "Does.Not.Exist"));
    }

    private static MarketplaceDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new MarketplaceDbContext(options);
    }
}
