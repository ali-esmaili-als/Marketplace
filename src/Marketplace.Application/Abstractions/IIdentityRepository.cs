using Marketplace.Domain.Identity;

namespace Marketplace.Application.Abstractions;

public interface IIdentityRepository
{
    Task<User?> GetUserByIdAsync(long userId, CancellationToken ct = default);
    Task<User?> GetUserByMobileAsync(string mobile, CancellationToken ct = default);
    Task<List<Rule>> GetActiveRulesForUserAsync(long userId, CancellationToken ct = default);
    Task<bool> HasPermissionAsync(long userId, string ruleCode, CancellationToken ct = default);
    void AddUser(User user);
    void AddRule(Rule rule);
    void AddUserRule(UserRule userRule);
}