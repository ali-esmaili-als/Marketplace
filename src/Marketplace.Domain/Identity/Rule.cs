using Marketplace.Domain.Common;

namespace Marketplace.Domain.Identity;

public sealed class Rule : AggregateRoot<long>
{
    private Rule() { }

    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public RuleActionType ActionType { get; private set; }
    public bool IsActive { get; private set; }

    public static Rule Create(long id, string code, string name, RuleActionType actionType)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(name))
            throw new DomainException("Rule data is invalid.");

        return new Rule
        {
            Id = id,
            Code = code.Trim(),
            Name = name.Trim(),
            ActionType = actionType,
            IsActive = true
        };
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}