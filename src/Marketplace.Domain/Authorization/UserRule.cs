using Marketplace.Domain.Common;
namespace Marketplace.Domain.Authorization;
public sealed class UserRule : Entity<long>
{
 private UserRule(){}
 public long UserId{get;private set;}
 public long RuleId{get;private set;}
 public bool IsActive{get;private set;}
 public static UserRule Create(long id,long userId,long ruleId)=>new(){Id=id,UserId=userId,RuleId=ruleId,IsActive=true};
 public void Disable()=>IsActive=false;
 public void Enable()=>IsActive=true;
}