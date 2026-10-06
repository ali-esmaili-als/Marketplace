using Marketplace.Domain.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Marketplace.Infrastructure.Authorization;

/// <summary>
/// Declares which user types are allowed to use this controller action.
/// The permission code is generated automatically as Controller.Action.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ActionAccessAttribute : Attribute, IAuthorizeData
{
    public ActionAccessAttribute(params UserTypeId[] userTypes)
    {
        if (userTypes is null || userTypes.Length == 0)
            throw new ArgumentException("At least one user type is required.", nameof(userTypes));

        UserTypes = userTypes.Distinct().ToArray();
    }

    public IReadOnlyList<UserTypeId> UserTypes { get; }

    public string? Policy { get; set; } = "ActionAccess";
    public string? Roles { get; set; }
    public string? AuthenticationSchemes { get; set; }
}
