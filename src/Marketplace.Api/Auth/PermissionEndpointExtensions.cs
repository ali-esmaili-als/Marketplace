namespace Marketplace.Api.Auth;

public static class PermissionEndpointExtensions
{
    public static RouteHandlerBuilder RequirePermission(this RouteHandlerBuilder builder, string permission)
        => builder.RequireAuthorization(PermissionPolicyProvider.Prefix + permission);
}