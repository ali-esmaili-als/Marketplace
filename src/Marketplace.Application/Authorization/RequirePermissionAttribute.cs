using Microsoft.AspNetCore.Authorization;
namespace Marketplace.Application.Authorization;
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
 public const string PolicyPrefix="Permission:";
 public RequirePermissionAttribute(string permission)=>Policy=PolicyPrefix+permission;
}