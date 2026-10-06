using Marketplace.Domain.Identity;

namespace Marketplace.Application.Authorization;

public sealed record AuthorizationRuleDto(
    long Id,
    string Code,
    string Name,
    byte RuleType,
    bool IsActive,
    IReadOnlyList<long> PermissionIds);

public sealed record CreateRuleRequest(
    string Code,
    string Name,
    byte RuleType,
    IReadOnlyList<long> PermissionIds);

public sealed record UpdateRulePermissionsRequest(
    IReadOnlyList<long> PermissionIds);

public sealed record AssignRuleRequest(
    long UserId,
    long RuleId);


public sealed record UserAuthorizationRulesDto(
    long UserId,
    IReadOnlyList<long> RuleIds);
