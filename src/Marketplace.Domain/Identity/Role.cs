using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public sealed class Role : AggregateRoot<long>
{
    private Role() { }
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }

    public static Role Create(long id, string name)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(name)) throw new DomainException("Invalid role.");
        return new Role { Id = id, Name = name.Trim(), IsActive = true };
    }
}