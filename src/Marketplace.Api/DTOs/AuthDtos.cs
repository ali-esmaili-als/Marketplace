namespace Marketplace.Api.DTOs;

public sealed record RegisterCustomerRequest(string Mobile, string Password, string DisplayName);
public sealed record LoginRequest(string Mobile, string Password);