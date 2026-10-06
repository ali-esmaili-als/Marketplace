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
    public bool IsVerified { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static SellerBankAccount Create(long id, long sellerId, string bankName, string iban, string accountHolderName)
    {
        if (id <= 0 || sellerId <= 0) throw new DomainException("Invalid bank account.");
        if (string.IsNullOrWhiteSpace(bankName) || string.IsNullOrWhiteSpace(iban) || string.IsNullOrWhiteSpace(accountHolderName))
            throw new DomainException("Bank account fields are required.");

        return new SellerBankAccount
        {
            Id = id, SellerId = sellerId, BankName = bankName.Trim(),
            Iban = iban.Trim().Replace(" ", "").ToUpperInvariant(),
            AccountHolderName = accountHolderName.Trim(), CreatedAtUtc = DateTime.UtcNow
        };
    }

    public void Verify() => IsVerified = true;
    public void SetDefault() => IsDefault = true;
    public void UnsetDefault() => IsDefault = false;
}
