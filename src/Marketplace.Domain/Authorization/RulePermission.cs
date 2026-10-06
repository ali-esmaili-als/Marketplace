using Marketplace.Domain.Common;
namespace Marketplace.Domain.Authorization;
public sealed class RulePermission : Entity<long>
{
 private RulePermission(){}
 public long RuleId{get;private set;}
 public long PermissionId{get;private set;}
 public static RulePermission Create(long id,long ruleId,long permissionId)=>new(){Id=id,RuleId=ruleId,PermissionId=permissionId};
}