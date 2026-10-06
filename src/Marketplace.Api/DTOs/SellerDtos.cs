namespace Marketplace.Api.DTOs;

public sealed record CreateStoreRequest(string Name, string Slug, string? Description);
public sealed record UpdateStoreRequest(string Name, string Slug, string? Description);
public sealed record AddBankAccountRequest(string BankName, string Iban, string AccountHolderName, bool MakeDefault);