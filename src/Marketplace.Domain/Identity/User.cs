using Marketplace.Domain.Common;
namespace Marketplace.Domain.Identity;
public sealed class User : AggregateRoot<long>
{
 private readonly List<UserTypeId> _userTypes=[]; private User(){}
 public string FirstName{get;private set;}=null!; public string LastName{get;private set;}=null!; public string? Mobile{get;private set;} public string? Email{get;private set;}
 public bool IsMobileVerified{get;private set;} public bool IsEmailVerified{get;private set;} public bool IsActive{get;private set;} public bool IsLocked{get;private set;} public DateTime? LockoutEndUtc{get;private set;} public DateTime? LastLoginAtUtc{get;private set;} public string SecurityStamp{get;private set;}=Guid.NewGuid().ToString("N"); public DateTime CreatedAtUtc{get;private set;} public DateTime UpdatedAtUtc{get;private set;}
 public IReadOnlyCollection<UserTypeId> UserTypes=>_userTypes.AsReadOnly();
 public static User Create(long id,string firstName,string lastName){if(string.IsNullOrWhiteSpace(firstName)||string.IsNullOrWhiteSpace(lastName))throw new DomainException("Name is required.");var n=DateTime.UtcNow;return new User{Id=id,FirstName=firstName.Trim(),LastName=lastName.Trim(),IsActive=true,CreatedAtUtc=n,UpdatedAtUtc=n};}
 public void AddUserType(UserTypeId t){if(!_userTypes.Contains(t))_userTypes.Add(t);Touch();} public void RemoveUserType(UserTypeId t){_userTypes.Remove(t);Touch();} public void SetMobile(string? v){Mobile=string.IsNullOrWhiteSpace(v)?null:v.Trim();IsMobileVerified=false;Touch();} public void SetEmail(string? v){Email=string.IsNullOrWhiteSpace(v)?null:v.Trim();IsEmailVerified=false;Touch();} public void MarkMobileVerified(){IsMobileVerified=true;Touch();} public void MarkEmailVerified(){IsEmailVerified=true;Touch();} public void RecordLogin(){LastLoginAtUtc=DateTime.UtcNow;Touch();} public void LockUntil(DateTime? u){IsLocked=true;LockoutEndUtc=u;Touch();} public void Unlock(){IsLocked=false;LockoutEndUtc=null;Touch();} public void Activate(){IsActive=true;Touch();} public void Deactivate(){IsActive=false;Touch();} private void Touch()=>UpdatedAtUtc=DateTime.UtcNow;
}
