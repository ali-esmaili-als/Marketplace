using Marketplace.Domain.Common;

namespace Marketplace.Domain.Catalog;

public sealed class Category : AggregateRoot<long>
{
    private Category() { }

    public long? ParentCategoryId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Slug { get; private set; } = null!;
    public string Path { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Category Create(long id, string name, string slug, string path, long? parentCategoryId = null)
    {
        if (id <= 0 || string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug) || string.IsNullOrWhiteSpace(path))
            throw new DomainException("Invalid category.");
        if (parentCategoryId == id) throw new DomainException("Category cannot be its own parent.");

        return new Category
        {
            Id = id, ParentCategoryId = parentCategoryId, Name = name.Trim(),
            Slug = slug.Trim(), Path = path.Trim(), IsActive = true, CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Move(long? parentCategoryId, string path)
    {
        if (parentCategoryId == Id) throw new DomainException("Category cannot be its own parent.");
        if (string.IsNullOrWhiteSpace(path)) throw new DomainException("Category path is required.");
        ParentCategoryId = parentCategoryId;
        Path = path.Trim();
    }

    public void Rename(string name, string slug)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(slug))
            throw new DomainException("Category name and slug are required.");
        Name = name.Trim();
        Slug = slug.Trim();
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
