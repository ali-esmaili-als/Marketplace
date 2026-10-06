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

    public Task<Rule?> GetRuleByCodeAsync(string code, CancellationToken ct = default)
        => db.Rules.SingleOrDefaultAsync(x => x.Code == code && x.IsActive, ct);

    public Task<Role?> GetRoleByNameAsync(string name, CancellationToken ct = default)
        => db.Roles.SingleOrDefaultAsync(x => x.Name == name && x.IsActive, ct);

    public Task<bool> HasPermissionAsync(long userId, string ruleCode, CancellationToken ct = default)
        => db.UserRules.Join(db.Rules,x=>x.RuleId,r=>r.Id,(ur,r)=>new { ur.UserId, r.IsActive, r.Code })
            .AnyAsync(x => x.UserId == userId && x.IsActive && x.Code == ruleCode, ct);

    public void AddUser(User user) => db.Users.Add(user);
    public void AddRule(Rule rule) => db.Rules.Add(rule);
    public void AddUserRule(UserRule userRule) => db.UserRules.Add(userRule);
    public void AddUserRoleAssignment(UserRoleAssignment assignment) => db.UserRoleAssignments.Add(assignment);

    public Task<UserRule?> GetUserRuleAsync(long userId, long ruleId, CancellationToken ct = default)
        => db.UserRules.SingleOrDefaultAsync(x => x.UserId == userId && x.RuleId == ruleId, ct);

    public Task<UserRoleAssignment?> GetUserRoleAssignmentAsync(long userId, long roleId, CancellationToken ct = default)
        => db.UserRoleAssignments.SingleOrDefaultAsync(x => x.UserId == userId && x.RoleId == roleId, ct);

    public void RemoveUserRule(UserRule item) => db.UserRules.Remove(item);
    public void RemoveUserRoleAssignment(UserRoleAssignment item) => db.UserRoleAssignments.Remove(item);
}