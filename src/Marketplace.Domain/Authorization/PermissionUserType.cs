using Marketplace.Domain.Common;
using Marketplace.Domain.Identity;
namespace Marketplace.Domain.Authorization;
public sealed class PermissionUserType : Entity<long>
{
 private PermissionUserType(){}
 public long PermissionId{get;private set;}
 public UserTypeId UserTypeId{get;private set;}
 public static PermissionUserType Create(long id,long permissionId,UserTypeId userTypeId)=>new(){Id=id,PermissionId=permissionId,UserTypeId=userTypeId};
}