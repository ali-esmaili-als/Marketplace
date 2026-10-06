using Marketplace.Application.Authorization;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Authorization;

public sealed class EfAuthorizationCatalogReader(MarketplaceDbContext db)
    : IAuthorizationCatalogReader
{
    public async Task<IReadOnlyList<AuthorizationActionDto>> GetActionsAsync(
        CancellationToken cancellationToken = default)
    {
        var permissions = await db.Permissions
            .AsNoTracking()
            .Where(x => x.IsActive)
            .OrderBy(x => x.Code)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                UserTypes = db.PermissionUserTypes
                    .Where(p => p.PermissionId == x.Id)
                    .Select(p => p.UserTypeId)
                    .ToArray()
            })
            .ToListAsync(cancellationToken);

        return permissions
            .Select(x =>
            {
                var separator = x.Code.IndexOf('.');
                var controller = separator > 0 ? x.Code[..separator] : x.Code;
                var action = separator > 0 ? x.Code[(separator + 1)..] : string.Empty;

                return new AuthorizationActionDto(
                    x.Id,
                    controller,
                    action,
                    x.Code,
                    x.Name,
                    x.UserTypes);
            })
            .ToArray();
    }
}
