using Marketplace.Domain.Common;

namespace Marketplace.Domain.Sellers;

public sealed class SellerBankAccount : Entity<long>
{
    private SellerBankAccount() { }

    public long SellerId { get; private set; }
    public string BankName { get; private set; } = null!;
    public string Iban { get; private set; } = null!;
    public string AccountHolderName { get; private set; } = null!;
    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static SellerBankAccount Create(long id, long sellerId, string bankName, string iban, string accountHolderName, bool isDefault)
    {
        if (string.IsNullOrWhiteSpace(iban)) throw new DomainException("IBAN is required.");
        if (string.IsNullOrWhiteSpace(accountHolderName)) throw new DomainException("Account holder name is required.");
        return new SellerBankAccount
        {
            Id = id, SellerId = sellerId, BankName = bankName.Trim(), Iban = iban.Trim().ToUpperInvariant(),
            AccountHolderName = accountHolderName.Trim(), IsDefault = isDefault, IsActive = true, CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void SetDefault() => IsDefault = true;
    public void UnsetDefault() => IsDefault = false;
    public void Deactivate() { IsActive = false; IsDefault = false; }
}
