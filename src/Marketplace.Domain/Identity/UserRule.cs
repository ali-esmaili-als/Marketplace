using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public sealed class UserRule : Entity<long>
{
    private UserRule() { }

    public long UserId { get; private set; }
    public long RuleId { get; private set; }
    public DateTime GrantedAtUtc { get; private set; }

    public static UserRule Create(long id, long userId, long ruleId)
    {
        if (id <= 0 || userId <= 0 || ruleId <= 0)
            throw new DomainException("Invalid user rule identifiers.");

        return new UserRule { Id = id, UserId = userId, RuleId = ruleId, GrantedAtUtc = DateTime.UtcNow };
    }
}