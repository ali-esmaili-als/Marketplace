namespace Marketplace.Api.DTOs;

public sealed record RegisterCustomerRequest(string Mobile, string Password, string DisplayName);
public sealed record LoginRequest(string Mobile, string Password);
public sealed record UserRuleRequest(string Code);
public sealed record UserRoleRequest(string RoleName);