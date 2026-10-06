using Marketplace.Application.Common.Abstractions;
using Marketplace.Domain.Identity;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Authorization;

public sealed class EfResourceAccess(
    MarketplaceDbContext db,
    ICurrentUser currentUser) : IResourceAccess
{
    public async Task EnsureAdminOrOwnerAsync(
        long ownerUserId,
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
            throw new UnauthorizedAccessException("Authentication is required.");

        if (ownerUserId == currentUser.UserId)
            return;

        var isAdmin = await db.UserUserTypes.AnyAsync(
            x => x.UserId == currentUser.UserId &&
                 x.UserTypeId == UserTypeId.Admin,
            cancellationToken);

        if (!isAdmin)
            throw new UnauthorizedAccessException("You do not have access to this resource.");
    }
}
