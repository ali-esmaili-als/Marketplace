using Marketplace.Application.Authorization;
using Marketplace.Application.Common.Abstractions;
using Marketplace.Domain.Authorization;
using Marketplace.Domain.Identity;
using Marketplace.Infrastructure.Authorization;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Application.Tests.Authorization;

public sealed class AuthorizationAdminServiceTests
{
    [Fact]
    public async Task Create_rule_and_assign_to_user_are_persisted()
    {
        await using var db = CreateDb();
        db.Users.Add(User.Create(10, "Ali", "Admin"));
        db.Permissions.Add(Permission.Create(100, "Refund.Create", "Refund / Create"));
        await db.SaveChangesAsync();

        var sut = new EfAuthorizationAdminService(db, new TestIdGenerator(1000));

        var ruleId = await sut.CreateRuleAsync(
            "refund-manager",
            "Refund Manager",
            1,
            [100]);

        await sut.AssignRuleAsync(10, ruleId);

        var rules = await sut.GetUserRulesAsync(10);

        Assert.Equal([ruleId], rules.RuleIds);
    }

    [Fact]
    public async Task Inactive_permission_cannot_be_added_to_rule()
    {
        await using var db = CreateDb();
        var permission = Permission.Create(100, "Refund.Create", "Refund / Create");
        permission.Disable();
        db.Permissions.Add(permission);
        await db.SaveChangesAsync();

        var sut = new EfAuthorizationAdminService(db, new TestIdGenerator(1000));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            sut.CreateRuleAsync("refund-manager", "Refund Manager", 1, [100]));
    }

    [Fact]
    public async Task Get_user_rules_returns_only_active_assignments()
    {
        await using var db = CreateDb();
        db.Users.Add(User.Create(10, "Ali", "Admin"));
        db.Rules.Add(Rule.Create(20, "refund-manager", "Refund Manager", 1));
        db.Rules.Add(Rule.Create(21, "inactive-rule", "Inactive Rule", 1));

        var active = UserRule.Create(30, 10, 20);
        var inactive = UserRule.Create(31, 10, 21);
        inactive.Disable();

        db.UserRules.AddRange(active, inactive);
        await db.SaveChangesAsync();

        var sut = new EfAuthorizationAdminService(db, new TestIdGenerator(1000));

        var result = await sut.GetUserRulesAsync(10);

        Assert.Equal([20L], result.RuleIds);
    }

    private static MarketplaceDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MarketplaceDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new MarketplaceDbContext(options);
    }

    private sealed class TestIdGenerator(long start) : IIdGenerator
    {
        private long _next = start;
        public long NewId() => _next++;
    }
}
