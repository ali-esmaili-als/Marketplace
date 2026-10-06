using Marketplace.Domain.Common;
namespace Marketplace.Domain.Authorization;
public sealed class Permission : Entity<long>
{
 private Permission(){}
 public string Code{get;private set;}=null!;
 public string Name{get;private set;}=null!;
 public bool IsActive{get;private set;}
 public static Permission Create(long id,string code,string name)=>new(){Id=id,Code=Normalize(code),Name=name.Trim(),IsActive=true};
 public void Disable()=>IsActive=false;
 public void Enable()=>IsActive=true;
 private static string Normalize(string value)=>string.IsNullOrWhiteSpace(value)?throw new DomainException("Permission code is required."):value.Trim();
}