using Marketplace.Application.Authorization;
using Marketplace.Application.Common.Abstractions;
using Marketplace.Domain.Authorization;
using Marketplace.Domain.Identity;
using Marketplace.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace Marketplace.Infrastructure.Authorization;

public sealed class AuthorizationCatalogSynchronizer(
    MarketplaceDbContext db,
    IIdGenerator idGenerator)
{
    public async Task SynchronizeAsync(Assembly apiAssembly, CancellationToken cancellationToken = default)
    {
        var discovered = Discover(apiAssembly);

        await using var transaction = await db.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable, cancellationToken);

        var permissions = await db.Permissions.ToListAsync(cancellationToken);
        var permissionByCode = permissions.ToDictionary(x => x.Code, StringComparer.OrdinalIgnoreCase);
        var discoveredCodes = discovered.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var endpoint in discovered)
        {
            if (!permissionByCode.TryGetValue(endpoint.Code, out var permission))
            {
                permission = Permission.Create(idGenerator.NewId(), endpoint.Code, endpoint.DisplayName);
                db.Permissions.Add(permission);
                permissionByCode.Add(endpoint.Code, permission);
            }
            else if (!permission.IsActive)
            {
                permission.Enable();
            }

            var existingTypes = await db.PermissionUserTypes
                .Where(x => x.PermissionId == permission.Id)
                .Select(x => x.UserTypeId)
                .ToListAsync(cancellationToken);

            foreach (var userType in endpoint.UserTypes)
            {
                if (!existingTypes.Contains(userType))
                {
                    db.PermissionUserTypes.Add(
                        PermissionUserType.Create(
                            idGenerator.NewId(),
                            permission.Id,
                            userType));
                }
            }

            var removedTypes = await db.PermissionUserTypes
                .Where(x => x.PermissionId == permission.Id && !endpoint.UserTypes.Contains(x.UserTypeId))
                .ToListAsync(cancellationToken);

            if (removedTypes.Count > 0)
                db.PermissionUserTypes.RemoveRange(removedTypes);
        }

        foreach (var permission in permissions)
        {
            if (!discoveredCodes.Contains(permission.Code))
                permission.Disable();
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static IReadOnlyList<DiscoveredEndpoint> Discover(Assembly assembly)
    {
        var result = new List<DiscoveredEndpoint>();

        foreach (var controller in assembly.GetTypes()
                     .Where(t => !t.IsAbstract && typeof(ControllerBase).IsAssignableFrom(t)))
        {
            var controllerName = controller.Name.EndsWith("Controller", StringComparison.Ordinal)
                ? controller.Name[..^"Controller".Length]
                : controller.Name;

            var controllerRequiresAuthorization =
                controller.GetCustomAttribute<AuthorizeAttribute>() is not null;

            foreach (var method in controller.GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                var access = method.GetCustomAttribute<ActionAccessAttribute>();
                var allowAnonymous = method.GetCustomAttribute<AllowAnonymousAttribute>() is not null;

                if (access is null)
                {
                    if (controllerRequiresAuthorization && !allowAnonymous)
                    {
                        throw new InvalidOperationException(
                            $"Controller action {controllerName}.{method.Name} is protected but has no ActionAccessAttribute.");
                    }

                    continue;
                }

                var code = controllerName + "." + method.Name;
                var displayName = controllerName + " / " + method.Name;

                result.Add(new DiscoveredEndpoint(code, displayName, access.UserTypes));
            }
        }

        return result
            .GroupBy(x => x.Code, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
    }

    private sealed record DiscoveredEndpoint(
        string Code,
        string DisplayName,
        IReadOnlyList<UserTypeId> UserTypes);
}
