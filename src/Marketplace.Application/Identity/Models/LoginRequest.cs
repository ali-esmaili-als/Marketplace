namespace Marketplace.Application.Identity.Models;
public sealed record LoginRequest(string Mobile,string Password);
public sealed record RefreshRequest(string RefreshToken);