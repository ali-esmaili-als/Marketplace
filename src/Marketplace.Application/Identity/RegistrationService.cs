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

        var user = User.Create(await ids.NextAsync(ct), mobile, "", displayName);
        user.SetPasswordHash(hasher.HashPassword(user, password));
        identity.AddUser(user);
        await uow.SaveChangesAsync(ct);
        return user.Id;
    }
}