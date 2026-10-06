using Marketplace.Domain.Identity;

namespace Marketplace.Application.Authorization;

public sealed record AuthorizationActionDto(
    long PermissionId,
    string Controller,
    string Action,
    string Code,
    string Name,
    IReadOnlyList<UserTypeId> UserTypes);
