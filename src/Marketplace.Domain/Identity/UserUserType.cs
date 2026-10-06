namespace Marketplace.Domain.Identity;
public sealed class UserUserType
{
 private UserUserType(){}
 public long UserId{get;private set;}
 public UserTypeId UserTypeId{get;private set;}
 public static UserUserType Create(long userId,UserTypeId userTypeId)=>new(){UserId=userId,UserTypeId=userTypeId};
}