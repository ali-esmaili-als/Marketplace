using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class Attribute : AggregateRoot<long>
{
    private Attribute() { }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public bool IsVariantAttribute { get; private set; }
    public bool IsActive { get; private set; }

    public static Attribute Create(long id, string name, string slug, bool isVariantAttribute)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug)) throw new DomainException("Attribute name and slug are required.");
        return new Attribute { Id=id, Name=name.Trim(), Slug=slug.Trim().ToLowerInvariant(), IsVariantAttribute=isVariantAttribute, IsActive=true };
    }

    public void SetVariantAttribute(bool value) => IsVariantAttribute=value;
}
