using Marketplace.Domain.Common;
namespace Marketplace.Domain.Identity;
public sealed class UserUserType : Entity<long>
{
 private UserUserType(){}
 public long UserId{get;private set;}
 public UserTypeId UserTypeId{get;private set;}
 public static UserUserType Create(long id,long userId,UserTypeId userTypeId)=>new(){Id=id,UserId=userId,UserTypeId=userTypeId};
}