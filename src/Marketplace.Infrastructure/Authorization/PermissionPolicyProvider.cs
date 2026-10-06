using Microsoft.AspNetCore.Authorization;
namespace Marketplace.Infrastructure.Authorization;
public sealed class PermissionPolicyProvider : IAuthorizationPolicyProvider
{
 public const string Prefix="Permission:";
 private readonly DefaultAuthorizationPolicyProvider fallback;
 public PermissionPolicyProvider(Microsoft.Extensions.Options.IOptions<AuthorizationOptions> options)=>fallback=new(options);
 public Task<AuthorizationPolicy?> GetDefaultPolicyAsync()=>fallback.GetDefaultPolicyAsync();
 public Task<AuthorizationPolicy?> GetFallbackPolicyAsync()=>fallback.GetFallbackPolicyAsync();
 public Task<AuthorizationPolicy?> GetPolicyAsync(string name)
 {
  if(!name.StartsWith(Prefix,StringComparison.OrdinalIgnoreCase))return fallback.GetPolicyAsync(name);
  var code=name[Prefix.Length..];
  var policy=new AuthorizationPolicyBuilder().AddRequirements(new PermissionRequirement(code)).Build();
  return Task.FromResult<AuthorizationPolicy?>(policy);
 }
}