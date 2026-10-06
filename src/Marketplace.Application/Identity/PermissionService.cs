using Marketplace.Application.Abstractions;

namespace Marketplace.Application.Identity;

public sealed class PermissionService(IIdentityRepository identity)
{
    public Task<bool> HasPermissionAsync(long userId, string code, CancellationToken ct = default)
        => identity.HasPermissionAsync(userId, code, ct);
}