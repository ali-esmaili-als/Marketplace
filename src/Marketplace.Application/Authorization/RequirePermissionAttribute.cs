using Microsoft.AspNetCore.Authorization;
namespace Marketplace.Application.Authorization;
public sealed class RequirePermissionAttribute(string permission) : AuthorizeAttribute
{
 public const string PolicyPrefix="Permission:";
 public RequirePermissionAttribute():this(""){}
 public string Permission=>Policy[PolicyPrefix.Length..];
 public RequirePermissionAttribute(string permission):base(){Policy=PolicyPrefix+permission;}
}