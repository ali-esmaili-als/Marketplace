namespace Marketplace.Application.Authorization;

public interface IAuthorizationAdminService
{
    Task<IReadOnlyList<AuthorizationRuleDto>> GetRulesAsync(
        CancellationToken cancellationToken = default);

    Task<long> CreateRuleAsync(
        string code,
        string name,
        byte ruleType,
        IReadOnlyList<long> permissionIds,
        CancellationToken cancellationToken = default);

    Task UpdateRulePermissionsAsync(
        long ruleId,
        IReadOnlyList<long> permissionIds,
        CancellationToken cancellationToken = default);

    Task AssignRuleAsync(
        long userId,
        long ruleId,
        CancellationToken cancellationToken = default);

    Task RemoveRuleAsync(
        long userId,
        long ruleId,
        CancellationToken cancellationToken = default);
}
