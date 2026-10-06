using Microsoft.EntityFrameworkCore;
using Marketplace.Application.Abstractions;
using Marketplace.Domain.Identity;

namespace Marketplace.Infrastructure.Persistence;

public sealed class IdentityRepository(MarketplaceDbContext db) : IIdentityRepository
{
    public Task<User?> GetUserByIdAsync(long userId, CancellationToken ct = default)
        => db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);

    public Task<User?> GetUserByMobileAsync(string mobile, CancellationToken ct = default)
        => db.Users.SingleOrDefaultAsync(x => x.Mobile == mobile, ct);

    public Task<List<Rule>> GetActiveRulesForUserAsync(long userId, CancellationToken ct = default)
        => db.UserRules.Where(x => x.UserId == userId)
            .Join(db.Rules.Where(x => x.IsActive), x => x.RuleId, x => x.Id, (_, rule) => rule)
            .OrderBy(x => x.Code).ToListAsync(ct);

    public Task<bool> HasPermissionAsync(long userId, string ruleCode, CancellationToken ct = default)
        => db.UserRules.AnyAsync(x => x.UserId == userId && x.Rule.IsActive && x.Rule.Code == ruleCode, ct);

    public void AddUser(User user) => db.Users.Add(user);
    public void AddRule(Rule rule) => db.Rules.Add(rule);
    public void AddUserRule(UserRule userRule) => db.UserRules.Add(userRule);
}