using Marketplace.Application.Abstractions;
using Marketplace.Domain.Common;
using Marketplace.Domain.Identity;

namespace Marketplace.Application.Identity;

public sealed class IdentityAdminService(IIdentityRepository identity, IUnitOfWork uow, IIdGenerator ids)
{
    public async Task GrantRuleAsync(long userId, string code, CancellationToken ct = default)
    {
        var user = await identity.GetUserByIdAsync(userId, ct) ?? throw new DomainException("User not found.");
        var rule = await identity.GetRuleByCodeAsync(code, ct) ?? throw new DomainException("Rule not found.");
        if (await identity.GetUserRuleAsync(user.Id, rule.Id, ct) is null)
            identity.AddUserRule(UserRule.Create(await ids.NextAsync(ct), user.Id, rule.Id));
        await uow.SaveChangesAsync(ct);
    }

    public async Task RevokeRuleAsync(long userId, string code, CancellationToken ct = default)
    {
        var rule = await identity.GetRuleByCodeAsync(code, ct) ?? throw new DomainException("Rule not found.");
        var assignment = await identity.GetUserRuleAsync(userId, rule.Id, ct);
        if (assignment is not null) identity.RemoveUserRule(assignment);
        await uow.SaveChangesAsync(ct);
    }

    public async Task AssignRoleAsync(long userId, string roleName, CancellationToken ct = default)
    {
        var user = await identity.GetUserByIdAsync(userId, ct) ?? throw new DomainException("User not found.");
        var role = await identity.GetRoleByNameAsync(roleName, ct) ?? throw new DomainException("Role not found.");
        if (await identity.GetUserRoleAssignmentAsync(user.Id, role.Id, ct) is null)
            identity.AddUserRoleAssignment(UserRoleAssignment.Create(await ids.NextAsync(ct), user.Id, role.Id));
        await uow.SaveChangesAsync(ct);
    }

    public async Task RevokeRoleAsync(long userId, string roleName, CancellationToken ct = default)
    {
        var role = await identity.GetRoleByNameAsync(roleName, ct) ?? throw new DomainException("Role not found.");
        var assignment = await identity.GetUserRoleAssignmentAsync(userId, role.Id, ct);
        if (assignment is not null) identity.RemoveUserRoleAssignment(assignment);
        await uow.SaveChangesAsync(ct);
    }

    public Task<List<Rule>> GetRulesAsync(long userId, CancellationToken ct = default)
        => identity.GetActiveRulesForUserAsync(userId, ct);
}