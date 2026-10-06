using Marketplace.Domain.Common;

namespace Marketplace.Domain.Shipping;

public sealed class DeliveryCity : AggregateRoot<long>
{
    private DeliveryCity() { }

    public string Name { get; private set; } = null!;
    public string ProvinceName { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static DeliveryCity Create(long id, string name, string provinceName, string code)
    {
        if (id <= 0) throw new DomainException("City identifier must be positive.");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(provinceName) || string.IsNullOrWhiteSpace(code))
            throw new DomainException("City name, province and code are required.");

        return new DeliveryCity
        {
            Id = id,
            Name = name.Trim(),
            ProvinceName = provinceName.Trim(),
            Code = code.Trim().ToUpperInvariant(),
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Rename(string name, string provinceName, string code)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(provinceName) || string.IsNullOrWhiteSpace(code))
            throw new DomainException("City name, province and code are required.");

        Name = name.Trim();
        ProvinceName = provinceName.Trim();
        Code = code.Trim().ToUpperInvariant();
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}