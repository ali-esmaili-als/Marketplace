using Marketplace.Application.Abstractions;
using Marketplace.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Marketplace.Application.Identity;

public sealed class RegistrationService(
    IIdentityRepository identity,
    IPasswordHasher<User> hasher,
    IIdGenerator ids,
    IUnitOfWork uow)
{
    public async Task<long> RegisterCustomerAsync(string mobile, string password, string displayName, CancellationToken ct = default)
    {
        if (await identity.GetUserByMobileAsync(mobile.Trim(), ct) is not null)
            throw new Marketplace.Domain.Common.DomainException("Mobile is already registered.");

        var user = User.Create(await ids.NextAsync(ct), mobile, "pending", displayName);
        user.SetPasswordHash(hasher.HashPassword(user, password));
        identity.AddUser(user);

        var customerRole = await identity.GetRoleByNameAsync("Customer", ct)
            ?? throw new Marketplace.Domain.Common.DomainException("Customer role is not configured.");
        identity.AddUserRoleAssignment(UserRoleAssignment.Create(await ids.NextAsync(ct), user.Id, customerRole.Id));

        foreach (var code in new[] { "Cart.Read", "Order.Create", "Order.ReadOwn" })
        {
            var rule = await identity.GetRuleByCodeAsync(code, ct)
                ?? throw new Marketplace.Domain.Common.DomainException($"Rule {code} is not configured.");
            identity.AddUserRule(UserRule.Create(await ids.NextAsync(ct), user.Id, rule.Id));
        }

        await uow.SaveChangesAsync(ct);
        return user.Id;
    }
}