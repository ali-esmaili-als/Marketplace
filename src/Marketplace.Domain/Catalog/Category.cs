using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class Category : AggregateRoot<long>
{
    private Category() { }

    public long? ParentId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string Path { get; private set; } = null!;
    public int SortOrder { get; private set; }
    public bool IsActive { get; private set; }

    public static Category Create(long id, string name, string slug, long? parentId, string path, int sortOrder = 0)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Category name is required.");
        if (string.IsNullOrWhiteSpace(slug)) throw new DomainException("Category slug is required.");
        if (string.IsNullOrWhiteSpace(path)) throw new DomainException("Category path is required.");
        return new Category { Id=id, Name=name.Trim(), Slug=slug.Trim().ToLowerInvariant(), ParentId=parentId, Path=path.Trim(), SortOrder=sortOrder, IsActive=true };
    }

    public void Update(string name, string slug, long? parentId, string path, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(path)) throw new DomainException("Category fields are required.");
        if (parentId == Id) throw new DomainException("A category cannot be its own parent.");
        Name=name.Trim(); Slug=slug.Trim().ToLowerInvariant(); ParentId=parentId; Path=path.Trim(); SortOrder=sortOrder;
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
