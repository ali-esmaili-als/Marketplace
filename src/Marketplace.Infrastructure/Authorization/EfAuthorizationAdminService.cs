using Marketplace.Application.Authorization;
using Marketplace.Domain.Authorization;
using Marketplace.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.Infrastructure.Authorization;

public sealed class EfAuthorizationAdminService(
    MarketplaceDbContext db,
    IIdGenerator idGenerator) : IAuthorizationAdminService
{
    public async Task<IReadOnlyList<AuthorizationRuleDto>> GetRulesAsync(
        CancellationToken cancellationToken = default)
    {
        var rules = await db.Rules
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new
            {
                x.Id,
                x.Code,
                x.Name,
                x.RuleType,
                x.IsActive,
                PermissionIds = db.RulePermissions
                    .Where(rp => rp.RuleId == x.Id)
                    .Select(rp => rp.PermissionId)
                    .ToArray()
            })
            .ToListAsync(cancellationToken);

        return rules
            .Select(x => new AuthorizationRuleDto(
                x.Id, x.Code, x.Name, x.RuleType, x.IsActive, x.PermissionIds))
            .ToArray();
    }

    public async Task<long> CreateRuleAsync(
        string code,
        string name,
        byte ruleType,
        IReadOnlyList<long> permissionIds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Rule code is required.", nameof(code));

        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Rule name is required.", nameof(name));

        var ids = permissionIds.Distinct().ToArray();

        var validCount = await db.Permissions
            .Where(x => x.IsActive && ids.Contains(x.Id))
            .CountAsync(cancellationToken);

        if (validCount != ids.Length)
            throw new InvalidOperationException("One or more permissions are invalid or inactive.");

        if (await db.Rules.AnyAsync(x => x.Code == code.Trim(), cancellationToken))
            throw new InvalidOperationException("Rule code already exists.");

        var rule = Rule.Create(idGenerator.NewId(), code, name, ruleType);
        db.Rules.Add(rule);

        foreach (var permissionId in ids)
        {
            db.RulePermissions.Add(
                RulePermission.Create(idGenerator.NewId(), rule.Id, permissionId));
        }

        await db.SaveChangesAsync(cancellationToken);
        return rule.Id;
    }

    public async Task UpdateRulePermissionsAsync(
        long ruleId,
        IReadOnlyList<long> permissionIds,
        CancellationToken cancellationToken = default)
    {
        var rule = await db.Rules.FirstOrDefaultAsync(x => x.Id == ruleId, cancellationToken)
            ?? throw new KeyNotFoundException("Rule not found.");

        if (!rule.IsActive)
            throw new InvalidOperationException("Inactive rule cannot be changed.");

        var ids = permissionIds.Distinct().ToArray();

        var validCount = await db.Permissions
            .Where(x => x.IsActive && ids.Contains(x.Id))
            .CountAsync(cancellationToken);

        if (validCount != ids.Length)
            throw new InvalidOperationException("One or more permissions are invalid or inactive.");

        var current = await db.RulePermissions
            .Where(x => x.RuleId == ruleId)
            .ToListAsync(cancellationToken);

        db.RulePermissions.RemoveRange(current);

        foreach (var permissionId in ids)
        {
            db.RulePermissions.Add(
                RulePermission.Create(idGenerator.NewId(), ruleId, permissionId));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task AssignRuleAsync(
        long userId,
        long ruleId,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Users.AnyAsync(x => x.Id == userId, cancellationToken))
            throw new KeyNotFoundException("User not found.");

        if (!await db.Rules.AnyAsync(x => x.Id == ruleId && x.IsActive, cancellationToken))
            throw new KeyNotFoundException("Active rule not found.");

        var assignment = await db.UserRules
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.RuleId == ruleId,
                cancellationToken);

        if (assignment is null)
        {
            db.UserRules.Add(UserRule.Create(idGenerator.NewId(), userId, ruleId));
        }
        else
        {
            assignment.Enable();
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<UserAuthorizationRulesDto> GetUserRulesAsync(
        long userId,
        CancellationToken cancellationToken = default)
    {
        if (!await db.Users.AnyAsync(x => x.Id == userId, cancellationToken))
            throw new KeyNotFoundException("User not found.");

        var ruleIds = await db.UserRules
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.IsActive)
            .Select(x => x.RuleId)
            .OrderBy(x => x)
            .ToArrayAsync(cancellationToken);

        return new UserAuthorizationRulesDto(userId, ruleIds);
    }

    public async Task RemoveRuleAsync(
        long userId,
        long ruleId,
        CancellationToken cancellationToken = default)
    {
        var assignment = await db.UserRules
            .FirstOrDefaultAsync(
                x => x.UserId == userId && x.RuleId == ruleId,
                cancellationToken);

        if (assignment is not null)
        {
            assignment.Disable();
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
