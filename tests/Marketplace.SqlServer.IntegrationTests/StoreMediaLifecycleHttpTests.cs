using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Marketplace.Api.Auth;
using Marketplace.Application.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Marketplace.SqlServer.IntegrationTests;

[Collection("FinancialIntegrityHttpTests")]
public sealed class StoreMediaLifecycleHttpTests : IAsyncLifetime, IDisposable
{
    private const string DatabaseName = "MarketplaceStoreMediaLifecycleHttpTests";
    private const string JwtKey = "Marketplace-Test-Only-Signing-Key-Must-Be-At-Least-32-Characters";
    private const string JwtIssuer = "Marketplace.IntegrationTests";
    private const string JwtAudience = "Marketplace.IntegrationTests";
    private const long SellerUserId = 71101;
    private const long OtherSellerUserId = 71102;
    private const long SellerId = 72101;
    private const long OtherSellerId = 72102;
    private const long StoreId = 73101;
    private const long OtherStoreId = 73102;

    private readonly string _baseConnectionString = Environment.GetEnvironmentVariable("MARKETPLACE_SQLSERVER")
        ?? throw new InvalidOperationException("MARKETPLACE_SQLSERVER must point to the SQL Server integration-test instance.");
    private readonly Dictionary<string, string?> _originalEnvironment = new();
    private readonly string _webRoot = Path.Combine(Path.GetTempPath(), "MarketplaceStoreMediaTests", Guid.NewGuid().ToString("N"));
    private string _connectionString = null!;
    private WebApplicationFactory<global::Program>? _factory;
    private HttpClient? _client;

    public async Task InitializeAsync()
    {
        var masterBuilder = new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = "master" };
        await using (var master = new SqlConnection(masterBuilder.ConnectionString))
        {
            await master.OpenAsync();
            await ExecuteAsync(master, $"""
                IF DB_ID(N'{DatabaseName}') IS NOT NULL
                BEGIN
                    ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                    DROP DATABASE [{DatabaseName}];
                END;
                CREATE DATABASE [{DatabaseName}];
                """);
        }

        _connectionString = new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = DatabaseName }.ConnectionString;
        try
        {
            Directory.CreateDirectory(_webRoot);
            var bootstrapPath = Path.Combine(AppContext.BaseDirectory, "database", "Marketplace_Complete.sql");
            Assert.True(File.Exists(bootstrapPath), $"Bootstrap SQL script was not copied to test output: {bootstrapPath}");
            await using (var connection = new SqlConnection(_connectionString))
            {
                await connection.OpenAsync();
                await ExecuteAsync(connection, await File.ReadAllTextAsync(bootstrapPath));
                await ExecuteAsync(connection, """
                    DECLARE @now DATETIME2(7)=SYSUTCDATETIME();
                    INSERT dbo.Users(Id,Mobile,PasswordHash,DisplayName,CreatedAtUtc)
                    VALUES (71101,N'09120007101',N'test-hash-media-one',N'Media Seller One',@now),
                           (71102,N'09120007102',N'test-hash-media-two',N'Media Seller Two',@now);
                    INSERT dbo.Sellers(Id,UserId,Status,CommissionRateBasisPoints,MinimumCommissionIRR,MaxStoreCount,CreatedAtUtc,ActivatedAtUtc)
                    VALUES (72101,71101,2,1000,0,3,@now,@now),
                           (72102,71102,2,1000,0,3,@now,@now);
                    INSERT dbo.Stores(Id,SellerId,Name,Slug,Status,CommissionRateBasisPoints,MinimumCommissionIRR,CreatedAtUtc)
                    VALUES (73101,72101,N'Media Test Store One',N'media-test-store-one',2,1000,0,@now),
                           (73102,72102,N'Media Test Store Two',N'media-test-store-two',2,1000,0,@now);
                    """);
            }

            SetEnvironment("Authentication__Jwt__Key", JwtKey);
            SetEnvironment("Authentication__Jwt__Issuer", JwtIssuer);
            SetEnvironment("Authentication__Jwt__Audience", JwtAudience);
            SetEnvironment("ConnectionStrings__Marketplace", _connectionString);

            _factory = new WebApplicationFactory<global::Program>().WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.UseWebRoot(_webRoot);
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Authentication:Jwt:Key"] = JwtKey,
                    ["Authentication:Jwt:Issuer"] = JwtIssuer,
                    ["Authentication:Jwt:Audience"] = JwtAudience,
                    ["ConnectionStrings:Marketplace"] = _connectionString
                }));
                builder.ConfigureTestServices(services =>
                {
                    foreach (var descriptor in services.Where(x =>
                        x.ImplementationType == typeof(global::MarketplaceMaintenanceHostedService) ||
                        x.ImplementationType == typeof(Marketplace.Api.StorefrontMediaCleanupHostedService) ||
                        x.ImplementationType == typeof(Marketplace.Infrastructure.Outbox.OutboxRetentionHostedService) ||
                        x.ImplementationType == typeof(PermissionHandler)).ToArray())
                        services.Remove(descriptor);

                    services.RemoveAll<IIdGenerator>();
                    services.AddSingleton<IIdGenerator, TestIdGenerator>();
                    services.AddSingleton<IAuthorizationHandler, TestPermissionHandler>();
                });
            });
            _client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(SellerUserId));
        }
        catch
        {
            await DropDatabaseAsync();
            throw;
        }
    }

    [Fact]
    public async Task Upload_public_gallery_order_and_delete_follow_complete_lifecycle()
    {
        using var first = CreateUpload("first.png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2 });
        using var firstResponse = await _client!.PostAsync($"/api/sellers/me/stores/{StoreId}/media?kind=logo", first);
        var uploadResponseBody = await firstResponse.Content.ReadAsStringAsync();
        Assert.True(firstResponse.StatusCode == HttpStatusCode.Created,
            $"Expected upload to return Created but received {(int)firstResponse.StatusCode} {firstResponse.StatusCode}. Response: {uploadResponseBody}");
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<MediaResponse>();
        Assert.NotNull(firstBody);
        Assert.Equal(0, firstBody.SortOrder);
        Assert.True(File.Exists(Path.Combine(_webRoot, firstBody.Url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));

        using var second = CreateUpload("second.png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 3, 4 });
        using var secondResponse = await _client.PostAsync($"/api/sellers/me/stores/{StoreId}/media?kind=logo", second);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        var secondBody = await secondResponse.Content.ReadFromJsonAsync<MediaResponse>();
        Assert.NotNull(secondBody);
        Assert.Equal(1, secondBody.SortOrder);

        using var galleryResponse = await _client.GetAsync($"/api/public/stores/{StoreId}/media");
        Assert.Equal(HttpStatusCode.OK, galleryResponse.StatusCode);
        var gallery = await galleryResponse.Content.ReadFromJsonAsync<List<MediaResponse>>();
        Assert.NotNull(gallery);
        Assert.Equal(new[] { firstBody.Id, secondBody.Id }, gallery.Select(x => x.Id).ToArray());

        using var reorderResponse = await _client.PutAsJsonAsync(
            $"/api/sellers/me/stores/{StoreId}/media/{secondBody.Id}/sort-order", new { sortOrder = 0 });
        Assert.Equal(HttpStatusCode.NoContent, reorderResponse.StatusCode);
        using var reorderedResponse = await _client.GetAsync($"/api/public/stores/{StoreId}/media");
        var reordered = await reorderedResponse.Content.ReadFromJsonAsync<List<MediaResponse>>();
        Assert.Equal(new[] { secondBody.Id, firstBody.Id }, reordered!.Select(x => x.Id).ToArray());

        using var deleteResponse = await _client.DeleteAsync($"/api/sellers/me/stores/{StoreId}/media/{secondBody.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
        Assert.False(File.Exists(Path.Combine(_webRoot, secondBody.Url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar))));
        using var finalResponse = await _client.GetAsync($"/api/public/stores/{StoreId}/media");
        var finalGallery = await finalResponse.Content.ReadFromJsonAsync<List<MediaResponse>>();
        var remainingMedia = Assert.Single(finalGallery!);
        Assert.Equal(firstBody.Id, remainingMedia.Id);
        Assert.Equal(0, remainingMedia.SortOrder);
    }

    [Fact]
    public async Task Seller_and_admin_theme_changes_are_persisted_to_public_storefront()
    {
        var sellerTheme = new
        {
            themeCode = "editorial",
            paletteCode = "forest",
            primaryColor = "#26734d",
            secondaryColor = "#f1faf4",
            backgroundColor = "#ffffff",
            textColor = "#183b2b",
            fontCode = "serif",
            cornerStyle = "round"
        };

        using var sellerUpdate = await _client!.PutAsJsonAsync($"/api/sellers/me/stores/{StoreId}/theme", sellerTheme);
        Assert.Equal(HttpStatusCode.OK, sellerUpdate.StatusCode);

        using var otherStoreUpdate = await _client.PutAsJsonAsync($"/api/sellers/me/stores/{OtherStoreId}/theme", sellerTheme);
        Assert.Equal(HttpStatusCode.NotFound, otherStoreUpdate.StatusCode);

        using var invalidThemeUpdate = await _client.PutAsJsonAsync($"/api/sellers/me/stores/{StoreId}/theme", new
        {
            themeCode = "unknown-layout",
            paletteCode = "forest",
            primaryColor = "#26734d",
            secondaryColor = "#f1faf4",
            backgroundColor = "#ffffff",
            textColor = "#183b2b",
            fontCode = "serif",
            cornerStyle = "round"
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalidThemeUpdate.StatusCode);

        using var publicAfterSeller = await _client.GetAsync($"/api/public/stores/{StoreId}/media-test-store-one");
        Assert.Equal(HttpStatusCode.OK, publicAfterSeller.StatusCode);
        using var sellerJson = System.Text.Json.JsonDocument.Parse(await publicAfterSeller.Content.ReadAsStringAsync());
        Assert.Equal("editorial", sellerJson.RootElement.GetProperty("themeCode").GetString());
        Assert.Equal("forest", sellerJson.RootElement.GetProperty("paletteCode").GetString());
        Assert.Equal("#26734d", sellerJson.RootElement.GetProperty("themePrimaryColor").GetString());
        Assert.Equal("serif", sellerJson.RootElement.GetProperty("themeFontCode").GetString());
        Assert.Equal("round", sellerJson.RootElement.GetProperty("themeCornerStyle").GetString());

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", CreateToken(SellerUserId, "Admin.Identity.Manage"));
        var adminTheme = new
        {
            themeCode = "luxe",
            paletteCode = "rose",
            primaryColor = "#a83269",
            secondaryColor = "#fff3f8",
            backgroundColor = "#ffffff",
            textColor = "#4a2036",
            fontCode = "modern",
            cornerStyle = "square"
        };
        using var adminUpdate = await _client.PutAsJsonAsync($"/api/admin/stores/{StoreId}/theme", adminTheme);
        Assert.Equal(HttpStatusCode.OK, adminUpdate.StatusCode);

        using var publicAfterAdmin = await _client.GetAsync($"/api/public/stores/{StoreId}/media-test-store-one");
        Assert.Equal(HttpStatusCode.OK, publicAfterAdmin.StatusCode);
        using var adminJson = System.Text.Json.JsonDocument.Parse(await publicAfterAdmin.Content.ReadAsStringAsync());
        Assert.Equal("luxe", adminJson.RootElement.GetProperty("themeCode").GetString());
        Assert.Equal("rose", adminJson.RootElement.GetProperty("paletteCode").GetString());
        Assert.Equal("#a83269", adminJson.RootElement.GetProperty("themePrimaryColor").GetString());
        Assert.Equal("modern", adminJson.RootElement.GetProperty("themeFontCode").GetString());
        Assert.Equal("square", adminJson.RootElement.GetProperty("themeCornerStyle").GetString());
    }

    [Fact]
    public async Task Upload_rejects_mismatched_image_signature_without_persisting_file()
    {
        using var content = CreateUpload("fake.png", "image/png", Encoding.UTF8.GetBytes("not a png file"));
        using var response = await _client!.PostAsync($"/api/sellers/me/stores/{StoreId}/media?kind=banner", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var galleryResponse = await _client.GetAsync($"/api/public/stores/{StoreId}/media");
        var gallery = await galleryResponse.Content.ReadFromJsonAsync<List<MediaResponse>>();
        Assert.Empty(gallery!);
        Assert.Empty(Directory.Exists(Path.Combine(_webRoot, "uploads", "storefront", StoreId.ToString()))
            ? Directory.GetFiles(Path.Combine(_webRoot, "uploads", "storefront", StoreId.ToString()))
            : Array.Empty<string>());
    }

    [Fact]
    public async Task Seller_cannot_upload_to_another_sellers_store()
    {
        using var content = CreateUpload("other.png", "image/png", new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        using var response = await _client!.PostAsync($"/api/sellers/me/stores/{OtherStoreId}/media?kind=logo", content);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static MultipartFormDataContent CreateUpload(string fileName, string contentType, byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(file, "file", fileName);
        return content;
    }

    private static string CreateToken(long userId, string permission = "Seller.Catalog.Manage")
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(JwtKey));
        var token = new JwtSecurityToken(JwtIssuer, JwtAudience,
            new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()), new Claim("test:permission", permission) },
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private static async Task ExecuteAsync(SqlConnection connection, string sql)
    {
        await using var command = new SqlCommand(sql, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private void SetEnvironment(string key, string value)
    {
        _originalEnvironment[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    private async Task DropDatabaseAsync()
    {
        if (string.IsNullOrWhiteSpace(_baseConnectionString)) return;
        var masterBuilder = new SqlConnectionStringBuilder(_baseConnectionString) { InitialCatalog = "master" };
        await using var master = new SqlConnection(masterBuilder.ConnectionString);
        await master.OpenAsync();
        await ExecuteAsync(master, $"""
            IF DB_ID(N'{DatabaseName}') IS NOT NULL
            BEGIN
                ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                DROP DATABASE [{DatabaseName}];
            END;
            """);
    }

    public async Task DisposeAsync()
    {
        _client?.Dispose();
        _factory?.Dispose();
        await DropDatabaseAsync();
    }

    public void Dispose()
    {
        foreach (var pair in _originalEnvironment)
            Environment.SetEnvironmentVariable(pair.Key, pair.Value);
        try { if (Directory.Exists(_webRoot)) Directory.Delete(_webRoot, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private sealed record MediaResponse(long Id, long StoreId, long? ProductId, string Kind, string Url, string ContentType, string? AltText, int SortOrder, DateTime CreatedAtUtc);

    private sealed class TestIdGenerator : IIdGenerator
    {
        private long _next = 990000;
        public Task<long> NextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Interlocked.Increment(ref _next));
    }

    private sealed class TestPermissionHandler : AuthorizationHandler<PermissionRequirement>
    {
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
        {
            if (context.User.HasClaim("test:permission", requirement.Permission))
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }
}
