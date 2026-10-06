using System.Security.Claims;
using Marketplace.Application.Common.Abstractions;
using Microsoft.AspNetCore.Http;

namespace Marketplace.Infrastructure.Checkout;

public sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public bool IsAuthenticated => accessor.HttpContext?.User.Identity?.IsAuthenticated == true;
    public long UserId
    {
        get
        {
            var value = accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? accessor.HttpContext?.User.FindFirstValue("sub");
            return long.TryParse(value, out var id) ? id : 0;
        }
    }
}