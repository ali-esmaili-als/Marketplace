using System.Security.Cryptography;
using System.Text;
using Marketplace.Domain.Common;

namespace Marketplace.Domain.Delivery;

public sealed class DeliveryCode : AggregateRoot<long>
{
    private DeliveryCode() { }
    public long OrderId { get; private set; }
    public byte[] CodeHash { get; private set; } = null!;
    public DeliveryCodeStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? ExpiresAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }

    public static DeliveryCode Create(long id,long orderId,string rawCode,DateTime? expiresAtUtc)
    {
        if(string.IsNullOrWhiteSpace(rawCode)) throw new DomainException("Delivery code is required.");
        if(expiresAtUtc.HasValue && expiresAtUtc<=DateTime.UtcNow) throw new DomainException("Delivery code expiry must be in the future.");
        return new DeliveryCode { Id=id, OrderId=orderId, CodeHash=Hash(rawCode), Status=DeliveryCodeStatus.Active, CreatedAtUtc=DateTime.UtcNow, ExpiresAtUtc=expiresAtUtc };
    }
    public bool Matches(string rawCode)=>Status==DeliveryCodeStatus.Active && (ExpiresAtUtc is null || ExpiresAtUtc>DateTime.UtcNow) && CryptographicOperations.FixedTimeEquals(CodeHash,Hash(rawCode));
    public void MarkUsed(){if(Status!=DeliveryCodeStatus.Active)throw new DomainException("Delivery code is not active.");Status=DeliveryCodeStatus.Used;UsedAtUtc=DateTime.UtcNow;}
    public void Expire(){if(Status!=DeliveryCodeStatus.Active)throw new DomainException("Delivery code is not active.");Status=DeliveryCodeStatus.Expired;}
    private static byte[] Hash(string value)=>SHA256.HashData(Encoding.UTF8.GetBytes(value.Trim()));
}
