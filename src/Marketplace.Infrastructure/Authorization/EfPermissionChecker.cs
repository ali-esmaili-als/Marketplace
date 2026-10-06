using Marketplace.Application.Authorization;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
namespace Marketplace.Infrastructure.Authorization;
public sealed class EfPermissionChecker(MarketplaceDbContext db):IPermissionChecker
{
 public async Task<bool> HasPermissionAsync(long userId,string permission,CancellationToken ct=default)
 {
  if (string.IsNullOrWhiteSpace(permission)) return false;

  var userActive = await db.Users.AsNoTracking()
   .Where(x => x.Id == userId)
   .Select(x => new { x.IsActive, x.IsLocked, x.LockoutEndUtc })
   .SingleOrDefaultAsync(ct);
  if (userActive is null || !userActive.IsActive || (userActive.IsLocked && (!userActive.LockoutEndUtc.HasValue || userActive.LockoutEndUtc > DateTime.UtcNow)))
   return false;
  var direct=await db.UserRules.AsNoTracking().Where(x=>x.UserId==userId&&x.IsActive)
   .Join(db.Rules.Where(x=>x.IsActive),ur=>ur.RuleId,r=>r.Id,(ur,r)=>r.Id)
   .Join(db.RulePermissions,rid=>rid,rp=>rp.RuleId,(rid,rp)=>rp.PermissionId)
   .Join(db.Permissions.Where(x=>x.IsActive&&x.Code==permission),pid=>pid,p=>p.Id,(pid,p)=>p.Id)
   .AnyAsync(ct);
  if(direct)return true;

  return await db.UserUserTypes.AsNoTracking().Where(x=>x.UserId==userId)
   .Join(db.PermissionUserTypes.Where(x=>true),ut=>ut.UserTypeId,put=>put.UserTypeId,(ut,put)=>put.PermissionId)
   .Join(db.Permissions.Where(x=>x.IsActive&&x.Code==permission),pid=>pid,p=>p.Id,(pid,p)=>p.Id)
   .AnyAsync(ct);
 }
}