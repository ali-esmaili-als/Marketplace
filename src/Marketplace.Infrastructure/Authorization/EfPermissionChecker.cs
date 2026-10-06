using Marketplace.Application.Authorization;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Marketplace.Infrastructure.Authorization;
public sealed class EfPermissionChecker(MarketplaceDbContext db):IPermissionChecker
{
 public Task<bool> HasPermissionAsync(long userId,string permission,CancellationToken ct=default)
  => db.UserRules.AsNoTracking().Where(x=>x.UserId==userId&&x.IsActive)
    .Join(db.Rules.Where(x=>x.IsActive),ur=>ur.RuleId,r=>r.Id,(ur,r)=>r)
    .Join(db.RulePermissions,r=>r.Id,rp=>rp.RuleId,(r,rp)=>rp)
    .Join(db.Permissions.Where(x=>x.IsActive),rp=>rp.PermissionId,p=>p.Id,(rp,p)=>p.Code)
    .AnyAsync(x=>x==permission,ct);
}