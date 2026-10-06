using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public sealed class UserRoleAssignment : Entity<long>
{
    private UserRoleAssignment() { }
    public long UserId { get; private set; }
    public long RoleId { get; private set; }

    public static UserRoleAssignment Create(long id, long userId, long roleId)
    {
        if (id <= 0 || userId <= 0 || roleId <= 0) throw new DomainException("Invalid user role.");
        return new UserRoleAssignment { Id = id, UserId = userId, RoleId = roleId };
    }
}