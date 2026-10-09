using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

/// <summary>
/// Exercises the actual ASP.NET Core authorization pipeline for financial-integrity routes.
/// These requests intentionally stop before querying financial data, so they do not mutate
/// or depend on the contents of the SQL Server database.
/// </summary>
public sealed class FinancialIntegrityAuthorizationHttpTests : IClassFixture<WebApplicationFactory<global::Program>>, IDisposable
{
    private const string JwtKey = "Marketplace-Test-Only-Signing-Key-Must-Be-At-Least-32-Characters";
    private const string JwtIssuer = "Marketplace.IntegrationTests";
    private const string JwtAudience = "Marketplace.IntegrationTests";
    private readonly HttpClient _client;
    private readonly Dictionary<string, string?> _originalEnvironment = new();

    public FinancialIntegrityAuthorizationHttpTests(WebApplicationFactory<global::Program> factory)
    {
        SetEnvironment("Authentication__Jwt__Key", JwtKey);
        SetEnvironment("Authentication__Jwt__Issuer", JwtIssuer);
        SetEnvironment("Authentication__Jwt__Audience", JwtAudience);
        SetEnvironment("ConnectionStrings__Marketplace", Environment.GetEnvironmentVariable("MARKETPLACE_SQLSERVER")
            ?? "Server=localhost;Database=MarketplaceAuthorizationTests;Integrated Security=true;TrustServerCertificate=True");

        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Jwt:Key"] = JwtKey,
                    ["Authentication:Jwt:Issuer"] = JwtIssuer,
                    ["Authentication:Jwt:Audience"] = JwtAudience,
                    ["ConnectionStrings:Marketplace"] = Environment.GetEnvironmentVariable("MARKETPLACE_SQLSERVER")
                        ?? "Server=localhost;Database=MarketplaceAuthorizationTests;Integrated Security=true;TrustServerCertificate=True"
                });
            });
            builder.ConfigureTestServices(services =>
            {
                foreach (var descriptor in services
                    .Where(x => x.ImplementationType == typeof(global::MarketplaceMaintenanceHostedService))
                    .ToArray())
                {
                    services.Remove(descriptor);
                }
            });
        }).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Theory]
    [InlineData("GET", "/api/admin/financial-integrity/summary")]
    [InlineData("GET", "/api/admin/financial-integrity/items")]
    [InlineData("GET", "/api/admin/financial-integrity/ledger")]
    [InlineData("GET", "/api/admin/financial-integrity/order-flows")]
    [InlineData("GET", "/api/admin/financial-integrity/order-trace/123")]
    [InlineData("GET", "/api/admin/financial-integrity/cases")]
    [InlineData("GET", "/api/admin/financial-integrity/cases/PaymentReview/123/history")]
    [InlineData("POST", "/api/admin/financial-integrity/reviews")]
    [InlineData("POST", "/api/admin/financial-integrity/cases")]
    [InlineData("POST", "/api/admin/financial-integrity/cases/PaymentReview/123/recheck")]
    public async Task Financial_integrity_routes_reject_anonymous_requests(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        if (method == "POST")
            request.Content = new StringContent("{}", Encoding.UTF8, "application/json");

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Financial_integrity_route_rejects_authenticated_token_without_numeric_user_identity()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(
            new Claim(ClaimTypes.NameIdentifier, "not-a-numeric-user-id")));

        using var response = await _client.GetAsync("/api/admin/financial-integrity/order-trace/123");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static string CreateToken(params Claim[] claims)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var token = new JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: claims,
            notBefore: DateTime.UtcNow.AddMinutes(-1),
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private void SetEnvironment(string key, string value)
    {
        _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    public void Dispose()
    {
        _client.Dispose();
        foreach (var pair in _originalEnvironment)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
    }
}
