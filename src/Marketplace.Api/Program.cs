using Marketplace.Application;
using Marketplace.Infrastructure;
using Marketplace.Api.Auth;
using Marketplace.Api.DTOs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;

var builder=WebApplication.CreateBuilder(args);

var jwtKey=builder.Configuration["Authentication:Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Authentication:Jwt:Key must be configured with at least 32 characters.");

var jwtIssuer=builder.Configuration["Authentication:Jwt:Issuer"] ?? "Marketplace";
var jwtAudience=builder.Configuration["Authentication:Jwt:Audience"] ?? "Marketplace.Client";

builder.Services.AddMarketplaceApplication();
builder.Services.AddMarketplaceInfrastructure(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Marketplace.Api.Auth.OtpAuthService>();
builder.Services.AddScoped<Marketplace.Application.Abstractions.ISmsProviderSettings, Marketplace.Infrastructure.Notifications.SmsProviderSettingsRepository>();
builder.Services.AddScoped<Marketplace.Application.Abstractions.ISmsProviderSettingsAdmin, Marketplace.Infrastructure.Notifications.SmsProviderSettingsAdminRepository>();
builder.Services.AddScoped<Marketplace.Application.Notifications.SmsProviderSettingsService>();
builder.Services.AddSingleton<Marketplace.Application.Abstractions.ISmsProvider, Marketplace.Infrastructure.Sms.TestSmsProvider>();
builder.Services.AddHttpClient<Marketplace.Infrastructure.Sms.KavenegarSmsProvider>();
builder.Services.AddTransient<Marketplace.Application.Abstractions.ISmsProvider>(sp => sp.GetRequiredService<Marketplace.Infrastructure.Sms.KavenegarSmsProvider>());
builder.Services.AddHttpClient<Marketplace.Infrastructure.Sms.SmsIrProvider>();
builder.Services.AddTransient<Marketplace.Application.Abstractions.ISmsProvider>(sp => sp.GetRequiredService<Marketplace.Infrastructure.Sms.SmsIrProvider>());
builder.Services.AddHttpClient<Marketplace.Infrastructure.Sms.MelipayamakSmsProvider>();
builder.Services.AddTransient<Marketplace.Application.Abstractions.ISmsProvider>(sp => sp.GetRequiredService<Marketplace.Infrastructure.Sms.MelipayamakSmsProvider>());
builder.Services.AddHttpClient<Marketplace.Infrastructure.Sms.HttpApiSmsProvider>();
builder.Services.AddTransient<Marketplace.Application.Abstractions.ISmsProvider>(sp => sp.GetRequiredService<Marketplace.Infrastructure.Sms.HttpApiSmsProvider>());

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = jwtIssuer,
            ValidateAudience = true, ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo { Title = "Marketplace API", Version = "v1" });
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization", Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = Microsoft.OpenApi.Models.ParameterLocation.Header
    });
    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme { Reference = new Microsoft.OpenApi.Models.OpenApiReference { Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme, Id = "Bearer" } },
            Array.Empty<string>()
        }
    });
});
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionHandler>();

builder.Services.AddHostedService<MarketplaceMaintenanceHostedService>();
var app=builder.Build();
if (app.Environment.IsDevelopment()) app.UseSwagger().UseSwaggerUI();
app.UseExceptionHandler(errorApp=>errorApp.Run(async context=>{var feature=context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();var ex=feature?.Error;var status=ex is Marketplace.Domain.Common.DomainException?StatusCodes.Status400BadRequest:StatusCodes.Status500InternalServerError;context.Response.StatusCode=status;context.Response.ContentType="application/problem+json";await context.Response.WriteAsJsonAsync(new{title=status==400?"Validation error":"Server error",detail=status==400?ex?.Message:"An unexpected error occurred."});}));

long CurrentUserId(System.Security.Claims.ClaimsPrincipal user)
    => long.TryParse(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : throw new UnauthorizedAccessException();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/api/notifications",async(System.Security.Claims.ClaimsPrincipal user,int? take,Marketplace.Application.Notifications.NotificationService service,CancellationToken ct)=>Results.Ok(await service.GetAsync(CurrentUserId(user),take??50,ct))).RequireAuthorization();
app.MapPost("/api/notifications/{notificationId:long}/read",async(System.Security.Claims.ClaimsPrincipal user,long notificationId,Marketplace.Application.Notifications.NotificationService service,CancellationToken ct)=>{await service.MarkReadAsync(CurrentUserId(user),notificationId,ct);return Results.NoContent();}).RequireAuthorization();
app.MapGet("/health",()=>Results.Ok(new{status="ok",utc=DateTime.UtcNow}));
app.MapGet("/health/db",async (Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,CancellationToken ct) =>
{
    var canConnect = await db.Database.CanConnectAsync(ct);
    return canConnect ? Results.Ok(new { status = "ok", database = "connected", utc = DateTime.UtcNow })
                      : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
});

app.MapGet("/api/auth/options", async (Marketplace.Api.Auth.OtpAuthService otp, CancellationToken ct) =>
{
    var options = await otp.GetOptionsAsync(ct);
    return Results.Ok(new { otpEnabled = options.OtpEnabled, providerName = options.ProviderName });
});

app.MapPost("/api/auth/otp/request", async (OtpRequest request, Marketplace.Api.Auth.OtpAuthService otp, CancellationToken ct) =>
    Results.Ok(await otp.RequestAsync(request.Mobile, ct)));

app.MapPost("/api/auth/otp/verify", async (OtpVerifyRequest request, Marketplace.Api.Auth.OtpAuthService otp, CancellationToken ct) =>
    Results.Ok(await otp.VerifyAsync(request.Mobile, request.Otp, ct)));

app.MapGet("/api/auth/me", async (System.Security.Claims.ClaimsPrincipal user, Marketplace.Application.Abstractions.IIdentityRepository identity, Marketplace.Application.Abstractions.ITokenService tokens, CancellationToken ct) =>
{
    var id = CurrentUserId(user);
    var current = await identity.GetUserByIdAsync(id, ct);
    if (current is null || !current.IsActive) return Results.Unauthorized();
    var roles = await tokens.GetRolesAsync(id, ct);
    var permissions = (await identity.GetActiveRulesForUserAsync(id, ct)).Select(x => x.Code).ToArray();
    return Results.Ok(new { id = current.Id.ToString(), mobile = current.Mobile, displayName = current.DisplayName, roles, permissions });
}).RequireAuthorization();

app.MapPost("/api/auth/register/customer",async(RegisterCustomerRequest request,Marketplace.Application.Identity.RegistrationService service,CancellationToken ct)=>
{
    var id=await service.RegisterCustomerAsync(request.Mobile,request.Password,request.DisplayName,ct);
    return Results.Ok(new { userId=id });
});

app.MapGet("/api/admin/users/{userId:long}/rules",async(long userId,Marketplace.Application.Identity.IdentityAdminService service,CancellationToken ct)=>
    Results.Ok(await service.GetRulesAsync(userId,ct)))
    .RequirePermission("Admin.Identity.Manage");

app.MapPost("/api/admin/users/{userId:long}/rules",async(long userId,UserRuleRequest request,Marketplace.Application.Identity.IdentityAdminService service,CancellationToken ct)=>{
    await service.GrantRuleAsync(userId,request.Code,ct); return Results.NoContent();
}).RequirePermission("Admin.Identity.Manage");

app.MapDelete("/api/admin/users/{userId:long}/rules",async(long userId,[Microsoft.AspNetCore.Mvc.FromBody] UserRuleRequest request,Marketplace.Application.Identity.IdentityAdminService service,CancellationToken ct)=>{
    await service.RevokeRuleAsync(userId,request.Code,ct); return Results.NoContent();
}).RequirePermission("Admin.Identity.Manage");

app.MapPost("/api/admin/users/{userId:long}/roles",async(long userId,UserRoleRequest request,Marketplace.Application.Identity.IdentityAdminService service,CancellationToken ct)=>{
    await service.AssignRoleAsync(userId,request.RoleName,ct); return Results.NoContent();
}).RequirePermission("Admin.Identity.Manage");

app.MapDelete("/api/admin/users/{userId:long}/roles",async(long userId,[Microsoft.AspNetCore.Mvc.FromBody] UserRoleRequest request,Marketplace.Application.Identity.IdentityAdminService service,CancellationToken ct)=>{
    await service.RevokeRoleAsync(userId,request.RoleName,ct); return Results.NoContent();
}).RequirePermission("Admin.Identity.Manage");

app.MapPost("/api/admin/sellers/{sellerId:long}/activate",async(long sellerId,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>{
    await service.ActivateAsync(sellerId,ct); return Results.NoContent();
}).RequirePermission("Admin.Seller.Manage");

app.MapPut("/api/admin/sellers/{sellerId:long}/store-limit",async(long sellerId,int maxStoreCount,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>{
    await service.ConfigureStoreLimitAsync(sellerId,maxStoreCount,ct); return Results.NoContent();
}).RequirePermission("Admin.Seller.Manage");

app.MapPost("/api/admin/sellers/{sellerId:long}/suspend",async(long sellerId,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>{await service.SuspendAsync(sellerId,ct);return Results.NoContent();}).RequirePermission("Admin.Seller.Manage");
app.MapPost("/api/admin/sellers/{sellerId:long}/reject",async(long sellerId,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>{await service.RejectAsync(sellerId,ct);return Results.NoContent();}).RequirePermission("Admin.Seller.Manage");
app.MapPut("/api/admin/sellers/{sellerId:long}/commission",async(long sellerId,CommissionConfigRequest request,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>{await service.ConfigureCommissionAsync(sellerId,request.RateBasisPoints,request.MinimumCommissionIRR,ct);return Results.NoContent();}).RequirePermission("Admin.Seller.Manage");

app.MapPost("/api/auth/login",async(LoginRequest request,Marketplace.Application.Identity.AuthenticationService service,CancellationToken ct)=>
{
    var result=await service.LoginAsync(request.Mobile,request.Password,ct);
    return Results.Ok(result);
});

app.MapGet("/api/payment-providers", async (Marketplace.Application.Abstractions.IPaymentGatewayFactory gateways, CancellationToken ct) =>
    Results.Ok(await gateways.GetAvailableAsync(ct)))
    .RequirePermission("Order.Create");

app.MapGet("/api/cart/summary", async (System.Security.Claims.ClaimsPrincipal user, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var userId = CurrentUserId(user);
    var cart = await db.Carts.AsNoTracking().Where(x => x.CustomerId == userId)
        .Select(x => new { x.Id, x.StoreId, x.SellerId }).SingleOrDefaultAsync(ct);
    if (cart is null) return Results.Ok((object)Array.Empty<object>());

    var rows = await (
        from item in db.CartItems.AsNoTracking()
        join product in db.Products.AsNoTracking() on item.ProductId equals product.Id
        join variant in db.ProductVariants.AsNoTracking() on item.ProductVariantId equals variant.Id
        join store in db.Stores.AsNoTracking() on product.StoreId equals store.Id
        join warrantyRow in db.Warranties.AsNoTracking() on item.WarrantyId equals (long?)warrantyRow.Id into warrantyGroup
        from warranty in warrantyGroup.DefaultIfEmpty()
        where item.CartId == cart.Id
        select new
        {
            item.Id, item.ProductId, item.ProductVariantId, item.WarrantyId, item.Quantity,
            product.Name, variant.SKU, variant.VariantKey, StoreName = store.Name,
            UnitPriceIRR = variant.PriceIRR ?? product.BasePriceIRR,
            WarrantyName = warranty == null ? null : warranty.Name,
            WarrantyPriceIRR = warranty == null ? 0L : warranty.PriceIRR
        }).ToListAsync(ct);
    return Results.Ok((object)rows);
}).RequirePermission("Cart.Read");

app.MapGet("/api/cart/items",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Cart.CartService service,CancellationToken ct)=>{
    var items=await service.GetItemsAsync(CurrentUserId(user),ct); return Results.Ok(items);
}).RequirePermission("Cart.Read");

app.MapPost("/api/cart/items",async(System.Security.Claims.ClaimsPrincipal user,CartItemRequest request,Marketplace.Application.Cart.CartService service,CancellationToken ct)=>{
    await service.AddItemAsync(CurrentUserId(user),request.SellerId,request.StoreId,request.ProductId,request.VariantId,request.Quantity,request.WarrantyId,ct);
    return Results.Ok();
}).RequirePermission("Cart.Read");

app.MapDelete("/api/cart/items/{variantId:long}",async(System.Security.Claims.ClaimsPrincipal user,long variantId,long? warrantyId,Marketplace.Application.Cart.CartService service,CancellationToken ct)=>{
    await service.RemoveItemAsync(CurrentUserId(user),variantId,warrantyId,ct); return Results.Ok();
}).RequirePermission("Cart.Read");


app.MapPost("/api/sellers/me/stores/{storeId:long}/attributes",async(System.Security.Claims.ClaimsPrincipal user,long storeId,AttributeRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();var id=await service.CreateAttributeAsync(seller.Id,storeId,request.Name,request.Slug,ct);return Results.Ok(new{id});}).RequirePermission("Seller.Catalog.Manage");
app.MapPost("/api/sellers/me/attributes/{attributeId:long}/values",async(System.Security.Claims.ClaimsPrincipal user,long attributeId,AttributeValueRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();var id=await service.CreateAttributeValueAsync(seller.Id,attributeId,request.Value,request.Slug,ct);return Results.Ok(new{id});}).RequirePermission("Seller.Catalog.Manage");
app.MapPost("/api/sellers/me/products/{productId:long}/attributes/{attributeId:long}",async(System.Security.Claims.ClaimsPrincipal user,long productId,long attributeId,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();await service.AssignAttributeAsync(seller.Id,productId,attributeId,ct);return Results.NoContent();}).RequirePermission("Seller.Catalog.Manage");
app.MapPost("/api/sellers/me/variants/{variantId:long}/attribute-values/{attributeValueId:long}",async(System.Security.Claims.ClaimsPrincipal user,long variantId,long attributeValueId,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();await service.AssignVariantValueAsync(seller.Id,variantId,attributeValueId,ct);return Results.NoContent();}).RequirePermission("Seller.Catalog.Manage");
app.MapGet("/api/categories",async(long? parentId,Marketplace.Application.Catalog.CategoryManagementService service,CancellationToken ct)=>Results.Ok(await service.GetChildrenAsync(parentId,ct)));
app.MapPost("/api/admin/categories",async(CategoryRequest request,Marketplace.Application.Catalog.CategoryManagementService service,CancellationToken ct)=>Results.Ok(new{id=await service.CreateAsync(request.Name,request.Slug,request.ParentCategoryId,ct)})).RequirePermission("Admin.Identity.Manage");
app.MapPut("/api/admin/categories/{categoryId:long}",async(long categoryId,CategoryRequest request,Marketplace.Application.Catalog.CategoryManagementService service,CancellationToken ct)=>{await service.RenameAsync(categoryId,request.Name,request.Slug,ct);return Results.NoContent();}).RequirePermission("Admin.Identity.Manage");
app.MapPost("/api/admin/categories/{categoryId:long}/deactivate",async(long categoryId,Marketplace.Application.Catalog.CategoryManagementService service,CancellationToken ct)=>{await service.DeactivateAsync(categoryId,ct);return Results.NoContent();}).RequirePermission("Admin.Identity.Manage");
app.MapGet("/api/shipping/cities",async(Marketplace.Application.Shipping.ShippingCoverageService service,CancellationToken ct)=>
    Results.Ok(await service.GetCitiesAsync(ct)));

app.MapPost("/api/sellers/apply",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>{
    var id=await service.ApplyAsync(CurrentUserId(user),ct); return Results.Ok(new { sellerId=id });
}).RequireAuthorization();

app.MapGet("/api/sellers/me/stores",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>
    Results.Ok(await service.GetMyStoresAsync(CurrentUserId(user),ct)))
    .RequirePermission("Seller.Shipping.Configure");

app.MapPost("/api/sellers/me/stores",async(System.Security.Claims.ClaimsPrincipal user,CreateStoreRequest request,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>{
    var id=await service.CreateStoreAsync(CurrentUserId(user),request.Name,request.Slug,request.Description,ct); return Results.Ok(new { storeId=id });
}).RequirePermission("Seller.Shipping.Configure");

app.MapPut("/api/sellers/me/stores/{storeId:long}",async(System.Security.Claims.ClaimsPrincipal user,long storeId,UpdateStoreRequest request,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>{
    await service.UpdateStoreAsync(CurrentUserId(user),storeId,request.Name,request.Slug,request.Description,ct); return Results.NoContent();
}).RequirePermission("Seller.Shipping.Configure");

app.MapPost("/api/sellers/me/stores/{storeId:long}/activate",async(System.Security.Claims.ClaimsPrincipal user,long storeId,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>{
    await service.ActivateStoreAsync(CurrentUserId(user),storeId,ct); return Results.NoContent();
}).RequirePermission("Seller.Shipping.Configure");

app.MapPost("/api/sellers/me/stores/{storeId:long}/close",async(System.Security.Claims.ClaimsPrincipal user,long storeId,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>{
    await service.CloseStoreAsync(CurrentUserId(user),storeId,ct); return Results.NoContent();
}).RequirePermission("Seller.Shipping.Configure");

app.MapGet("/api/sellers/me/bank-accounts",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>
    Results.Ok(await service.GetMyBankAccountsAsync(CurrentUserId(user),ct)))
    .RequirePermission("Seller.Settlement.Request");

app.MapPost("/api/sellers/me/bank-accounts",async(System.Security.Claims.ClaimsPrincipal user,AddBankAccountRequest request,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>{
    var id=await service.AddBankAccountAsync(CurrentUserId(user),request.BankName,request.Iban,request.AccountHolderName,request.MakeDefault,ct); return Results.Ok(new { bankAccountId=id });
}).RequirePermission("Seller.Settlement.Request");

app.MapPost("/api/sellers/me/bank-accounts/{accountId:long}/default",async(System.Security.Claims.ClaimsPrincipal user,long accountId,Marketplace.Application.Sellers.SellerManagementService service,CancellationToken ct)=>{
    await service.SetDefaultBankAccountAsync(CurrentUserId(user),accountId,ct); return Results.NoContent();
}).RequirePermission("Seller.Settlement.Request");

app.MapGet("/api/stores/{storeId:long}/shipping-cities",async(long storeId,Marketplace.Application.Shipping.ShippingCoverageService service,CancellationToken ct)=>
    Results.Ok(await service.GetStoreCitiesAsync(storeId,ct)));

app.MapPut("/api/stores/{storeId:long}/shipping-cities",async(System.Security.Claims.ClaimsPrincipal user,long storeId,StoreShippingCitiesRequest request,Marketplace.Application.Shipping.ShippingCoverageService service,CancellationToken ct)=>{
    await service.ConfigureStoreCitiesAsync(CurrentUserId(user),storeId,request.CityIds,ct); return Results.NoContent();
}).RequirePermission("Seller.Shipping.Configure");

app.MapGet("/api/payments/providers",async(Marketplace.Application.Abstractions.IPaymentProviderSettings settings,CancellationToken ct)=>
    Results.Ok(await settings.GetAvailableAsync(ct)));

app.MapGet("/api/admin/sms-providers",async(Marketplace.Application.Notifications.SmsProviderSettingsService service,CancellationToken ct)=>
    Results.Ok(await service.GetAllAsync(ct))).RequirePermission("Admin.SmsProviders.Read");
app.MapPut("/api/admin/sms-providers/{provider}",async(string provider,SmsProviderConfigureRequest request,Marketplace.Application.Notifications.SmsProviderSettingsService service,CancellationToken ct)=>{
    await service.ConfigureAsync(provider,request.IsEnabled,request.IsVisible,request.SortOrder,ct);
    return Results.NoContent();
}).RequirePermission("Admin.SmsProviders.Configure");

app.MapGet("/api/admin/payment-providers",async(Marketplace.Application.Payments.PaymentProviderSettingsService service,CancellationToken ct)=>
    Results.Ok(await service.GetAllAsync(ct))).RequirePermission("Admin.PaymentProviders.Read");

app.MapPut("/api/admin/payment-providers/{provider}",async(Marketplace.Domain.Payments.PaymentProviderCode provider,PaymentProviderConfigureRequest request,Marketplace.Application.Payments.PaymentProviderSettingsService service,CancellationToken ct)=>{
    await service.ConfigureAsync(provider,request.IsEnabled,request.IsVisible,request.SortOrder,request.ConfigurationJson,ct);
    return Results.NoContent();
}).RequirePermission("Admin.PaymentProviders.Configure");

app.MapGet("/api/sellers/me/finance", async (System.Security.Claims.ClaimsPrincipal user, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var userId = CurrentUserId(user);
    var sellerId = await db.Sellers.AsNoTracking().Where(s => s.UserId == userId).Select(s => (long?)s.Id).SingleOrDefaultAsync(ct);
    if (sellerId is null) return Results.NotFound(new { detail = "Seller profile not found." });
    var balance = await db.SellerBalances.AsNoTracking().Where(x => x.SellerId == sellerId.Value)
        .Select(x => new { x.AvailableIRR, x.PendingIRR, x.BlockedIRR, x.ReservedForSettlementIRR, x.LiabilityIRR, x.UpdatedAtUtc })
        .SingleOrDefaultAsync(ct);
    var available = balance?.AvailableIRR ?? 0L;
    var reserved = balance?.ReservedForSettlementIRR ?? 0L;
    return Results.Ok(new {
        sellerId = sellerId.Value, availableIRR = available, pendingIRR = balance?.PendingIRR ?? 0L,
        blockedIRR = balance?.BlockedIRR ?? 0L, reservedForSettlementIRR = reserved,
        liabilityIRR = balance?.LiabilityIRR ?? 0L, withdrawableIRR = Math.Max(0L, available - reserved),
        updatedAtUtc = balance?.UpdatedAtUtc
    });
}).RequirePermission("Seller.Settlement.Request");

app.MapGet("/api/sellers/me/finance/transactions", async (
    System.Security.Claims.ClaimsPrincipal user, DateTime? fromUtc, DateTime? toUtc,
    string? type, long? orderId, int? take,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var userId = CurrentUserId(user);
    var sellerId = await db.Sellers.AsNoTracking().Where(s => s.UserId == userId)
        .Select(s => (long?)s.Id).SingleOrDefaultAsync(ct);
    if (sellerId is null) return Results.NotFound(new { detail = "Seller profile not found." });
    if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
        return Results.BadRequest(new { detail = "fromUtc must be earlier than toUtc." });

    var query = db.BalanceTransactions.AsNoTracking().Where(x => x.SellerId == sellerId.Value);
    if (fromUtc.HasValue) query = query.Where(x => x.CreatedAtUtc >= fromUtc.Value);
    if (toUtc.HasValue) query = query.Where(x => x.CreatedAtUtc < toUtc.Value);
    if (orderId.HasValue) query = query.Where(x => x.OrderId == orderId.Value);
    if (!string.IsNullOrWhiteSpace(type) && Enum.TryParse<Marketplace.Domain.Finance.BalanceTransactionType>(type, true, out var parsedType))
        query = query.Where(x => x.Type == parsedType);
    else if (!string.IsNullOrWhiteSpace(type))
        return Results.BadRequest(new { detail = "Unknown transaction type." });

    var limit = Math.Clamp(take ?? 50, 1, 100);
    var rows = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
        .Take(limit).Select(x => new {
            id = x.Id, orderId = x.OrderId, settlementId = x.SettlementId,
            type = x.Type.ToString(), bucket = x.Bucket.ToString(), amountIRR = x.AmountIRR,
            balanceBeforeIRR = x.BalanceBeforeIRR, balanceAfterIRR = x.BalanceAfterIRR,
            reference = x.Reference, createdAtUtc = x.CreatedAtUtc
        }).ToListAsync(ct);
    return Results.Ok(rows);
}).RequirePermission("Seller.Settlement.Request");

app.MapGet("/api/sellers/me/settlements", async (System.Security.Claims.ClaimsPrincipal user, int? take, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var userId = CurrentUserId(user);
    var sellerId = await db.Sellers.AsNoTracking().Where(s => s.UserId == userId).Select(s => (long?)s.Id).SingleOrDefaultAsync(ct);
    if (sellerId is null) return Results.NotFound(new { detail = "Seller profile not found." });
    var limit = Math.Clamp(take ?? 20, 1, 100);
    var rows = await db.Settlements.AsNoTracking().Where(x => x.SellerId == sellerId.Value)
        .OrderByDescending(x => x.RequestedAtUtc).Take(limit).ToListAsync(ct);
    return Results.Ok(rows.Select(x => new {
        id = x.Id, amountIRR = x.AmountIRR, status = x.Status.ToString(), bankName = x.BankNameSnapshot,
        iban = x.IbanSnapshot, accountHolderName = x.AccountHolderNameSnapshot, reference = x.Reference,
        failureReason = x.FailureReason, requestedAtUtc = x.RequestedAtUtc, completedAtUtc = x.CompletedAtUtc
    }));
}).RequirePermission("Seller.Settlement.Request");

app.MapGet("/api/admin/settlements", async (string? status, int? take, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var query = db.Settlements.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(status))
    {
        if (!Enum.TryParse<Marketplace.Domain.Finance.SettlementStatus>(status, true, out var parsedStatus))
            return Results.BadRequest(new { detail = "Invalid settlement status." });
        query = query.Where(x => x.Status == parsedStatus);
    }

    var limit = Math.Clamp(take ?? 50, 1, 100);
    var items = await query.OrderByDescending(x => x.RequestedAtUtc).Take(limit)
        .Select(x => new
        {
            id = x.Id, sellerId = x.SellerId, amountIRR = x.AmountIRR, status = x.Status.ToString(),
            bankName = x.BankNameSnapshot, iban = x.IbanSnapshot, accountHolderName = x.AccountHolderNameSnapshot,
            reference = x.Reference, failureReason = x.FailureReason,
            requestedAtUtc = x.RequestedAtUtc, completedAtUtc = x.CompletedAtUtc
        }).ToListAsync(ct);
    return Results.Ok(items);
}).RequirePermission("Admin.Settlement.Process");

app.MapPost("/api/settlements",async(System.Security.Claims.ClaimsPrincipal user,SettlementRequest request,Marketplace.Application.Settlements.SettlementService service,CancellationToken ct)=>{
    var result=await service.RequestAsync(CurrentUserId(user),request.BankAccountId,request.AmountIRR,ct); return Results.Ok(result);
}).RequirePermission("Seller.Settlement.Request");

app.MapPost("/api/settlements/{settlementId:long}/process",async(long settlementId,Marketplace.Application.Settlements.SettlementService service,CancellationToken ct)=>{
    var result=await service.ProcessAsync(settlementId,ct);
    return result.Status=="Completed" ? Results.Ok(result) : Results.BadRequest(result);
}).RequirePermission("Admin.Settlement.Process");

app.MapPost("/api/admin/settlements/{settlementId:long}/reconcile", async (long settlementId, SettlementReconciliationRequest request, Marketplace.Application.Settlements.SettlementService service, CancellationToken ct) =>
{
    var result = await service.ReconcileAsync(settlementId, request.TransferCompleted, request.BankReference, request.Note, ct);
    return Results.Ok(result);
}).RequirePermission("Admin.Settlement.Process");

app.MapPost("/api/orders/checkout",async(System.Security.Claims.ClaimsPrincipal user,CheckoutRequest request,Marketplace.Application.Orders.OrderCreationService service,CancellationToken ct)=>{
    var result=await service.CheckoutAsync(CurrentUserId(user),request.Provider,request.DestinationCityId,request.CouponCode,ct); return Results.Ok(result);
}).RequirePermission("Order.Create");

app.MapGet("/api/payments/test-return", async (long paymentId, string authority, string? result,
    Marketplace.Application.Orders.PaymentVerificationService service, CancellationToken ct) =>
{
    var verified = await service.VerifyTestReturnAsync(paymentId, authority,
        string.Equals(result, "success", StringComparison.OrdinalIgnoreCase), ct);
    var heading = verified.Paid ? "پرداخت آزمایشی موفق بود" : "پرداخت آزمایشی ناموفق بود";
    var message = verified.Paid
        ? "پرداخت ثبت شد. می‌توانید به فروشگاه برگردید."
        : (verified.Error ?? "پرداخت انجام نشد.");
    return Results.Content($"<!doctype html><html lang=\"fa\" dir=\"rtl\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>{heading}</title><body style=\"font-family:Tahoma,sans-serif;max-width:620px;margin:10vh auto;padding:24px;line-height:2\"><h1>{heading}</h1><p>{System.Net.WebUtility.HtmlEncode(message)}</p><p>شناسه پرداخت: {paymentId}</p></body></html>", "text/html; charset=utf-8",
        System.Text.Encoding.UTF8, verified.Paid ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
});

app.MapPost("/api/payments/{paymentId:long}/verify",async(System.Security.Claims.ClaimsPrincipal user,long paymentId,PaymentVerifyRequest request,Marketplace.Application.Orders.PaymentVerificationService service,CancellationToken ct)=>{
    var result=await service.VerifyAsync(CurrentUserId(user),paymentId,request.Authority,ct);
    return result.Paid ? Results.Ok(result) : Results.BadRequest(result);
}).RequirePermission("Order.Create");

app.MapPost("/api/seller/orders/{orderId:long}/delivery/ready",async(System.Security.Claims.ClaimsPrincipal user,long orderId,Marketplace.Application.Orders.OrderActorService service,CancellationToken ct)=>{await service.ReadyAsync(CurrentUserId(user),orderId,ct);return Results.Ok();}).RequirePermission("Order.Delivery.Confirm");

app.MapPost("/api/seller/orders/{orderId:long}/delivery/confirm",async(System.Security.Claims.ClaimsPrincipal user,long orderId,DeliveryConfirmRequest request,Marketplace.Application.Orders.OrderActorService service,CancellationToken ct)=>{await service.DeliverAsync(CurrentUserId(user),orderId,request.Code,request.Reference,request.DeliveredAtUtc,request.ComplaintExpiresAtUtc,ct);return Results.Ok();}).RequirePermission("Order.Delivery.Confirm");

app.MapPost("/api/seller/orders/{orderId:long}/delivery/expire",async(System.Security.Claims.ClaimsPrincipal user,long orderId,Marketplace.Application.Orders.OrderActorService service,CancellationToken ct)=>{await service.ExpireAsync(CurrentUserId(user),orderId,ct);return Results.Ok();}).RequirePermission("Order.Delivery.Confirm");

app.MapPost("/api/orders/{orderId:long}/complaints",async(System.Security.Claims.ClaimsPrincipal user,long orderId,ComplaintRequest request,Marketplace.Application.Orders.OrderActorService service,CancellationToken ct)=>{var id=await service.ComplaintAsync(CurrentUserId(user),orderId,request.Reason,ct);return Results.Ok(new{id});}).RequirePermission("Order.Create");

app.MapPost("/api/complaints/{complaintId:long}/resolve",async(long complaintId,ComplaintResolveRequest request,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    await service.ResolveComplaintAsync(complaintId,request.CustomerWon,request.Note,ct);return Results.Ok();
}).RequirePermission("Complaint.Resolve");

app.MapPost("/api/orders/{orderId:long}/refund",async(System.Security.Claims.ClaimsPrincipal user,long orderId,RefundRequest request,Marketplace.Application.Orders.OrderActorService service,CancellationToken ct)=>{await service.RefundAsync(CurrentUserId(user),orderId,request.Reason,ct);return Results.Ok();}).RequirePermission("Order.Create");


app.MapPost("/api/sellers/me/campaigns",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Pricing.CreateCampaignRequest request,Marketplace.Application.Pricing.PricingManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
    var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();
    var variants=request.VariantIds??Array.Empty<long>();
    var targets=request.ProductIds.Select((p,i)=>(p, i<variants.Length && variants[i]>0 ? (long?)variants[i] : null)).ToArray();
    var id=await service.CreateCampaignAsync(seller.Id,request.StoreId,request.Name,request.DiscountType,request.DiscountValue,request.StartsAtUtc,request.EndsAtUtc,targets,ct);
    return Results.Ok(new { id });
}).RequirePermission("Seller.Campaign.Manage");

app.MapPost("/api/sellers/me/coupons",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Pricing.CreateCouponRequest request,Marketplace.Application.Pricing.PricingManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
    var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();
    var id=await service.CreateCouponAsync(seller.Id,request.StoreId,request.Code,request.DiscountType,request.DiscountValue,request.MaxDiscountAmountIRR,request.MinimumPurchaseIRR,request.MaxUses,request.NewCustomerOnly,request.StartsAtUtc,request.EndsAtUtc,request.ProductIds??Array.Empty<long>(),request.CategoryIds??Array.Empty<long>(),ct);
    return Results.Ok(new { id });
}).RequirePermission("Seller.Coupon.Manage");


app.MapGet("/api/sellers/me/stores/{storeId:long}/products",async(System.Security.Claims.ClaimsPrincipal user,long storeId,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();
 return Results.Ok(await service.GetProductsAsync(seller.Id,storeId,ct));
}).RequirePermission("Seller.Catalog.Manage");

app.MapPost("/api/sellers/me/stores/{storeId:long}/products",async(System.Security.Claims.ClaimsPrincipal user,long storeId,CatalogProductRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();
 var id=await service.CreateProductAsync(seller.Id,storeId,request.CategoryId,request.Name,request.Slug,request.Description,request.BasePriceIRR,request.HasVariants,ct);
 return Results.Ok(new{id});
}).RequirePermission("Seller.Catalog.Manage");

app.MapPut("/api/sellers/me/products/{productId:long}",async(System.Security.Claims.ClaimsPrincipal user,long productId,CatalogProductUpdateRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();await service.UpdateProductAsync(seller.Id,productId,request.Name,request.Slug,request.Description,request.BasePriceIRR,ct);return Results.NoContent();}).RequirePermission("Seller.Catalog.Manage");
app.MapPost("/api/sellers/me/products/{productId:long}/activate",async(System.Security.Claims.ClaimsPrincipal user,long productId,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException(); await service.ActivateProductAsync(seller.Id,productId,ct); return Results.NoContent();
}).RequirePermission("Seller.Catalog.Manage");

app.MapPost("/api/sellers/me/products/{productId:long}/deactivate",async(System.Security.Claims.ClaimsPrincipal user,long productId,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException(); await service.DeactivateProductAsync(seller.Id,productId,ct); return Results.NoContent();
}).RequirePermission("Seller.Catalog.Manage");

app.MapGet("/api/sellers/me/products/{productId:long}/variants",async(System.Security.Claims.ClaimsPrincipal user,long productId,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException(); return Results.Ok(await service.GetVariantsAsync(seller.Id,productId,ct));
}).RequirePermission("Seller.Catalog.Manage");

app.MapPost("/api/sellers/me/products/{productId:long}/variants",async(System.Security.Claims.ClaimsPrincipal user,long productId,CatalogVariantRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException(); var id=await service.AddVariantAsync(seller.Id,productId,request.SKU,request.VariantKey,request.PriceIRR,ct); return Results.Ok(new{id});
}).RequirePermission("Seller.Catalog.Manage");

app.MapPut("/api/sellers/me/variants/{variantId:long}",async(System.Security.Claims.ClaimsPrincipal user,long variantId,CatalogVariantRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException(); await service.UpdateVariantAsync(seller.Id,variantId,request.SKU,request.VariantKey,request.PriceIRR,request.IsActive,ct); return Results.NoContent();
}).RequirePermission("Seller.Catalog.Manage");

app.MapGet("/api/sellers/me/variants/{variantId:long}/stock",async(System.Security.Claims.ClaimsPrincipal user,long variantId,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();var stock=await service.GetStockAsync(seller.Id,variantId,ct);return Results.Ok(new { stockQuantity=stock.StockQuantity,reservedQuantity=stock.ReservedQuantity,availableQuantity=stock.AvailableQuantity });}).RequirePermission("Seller.Catalog.Manage");
app.MapPut("/api/sellers/me/variants/{variantId:long}/stock",async(System.Security.Claims.ClaimsPrincipal user,long variantId,StockRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException(); await service.SetStockAsync(seller.Id,variantId,request.Quantity,ct); return Results.NoContent();
}).RequirePermission("Seller.Catalog.Manage");

app.MapPost("/api/sellers/me/stores/{storeId:long}/warranties",async(System.Security.Claims.ClaimsPrincipal user,long storeId,WarrantyRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException(); var id=await service.CreateWarrantyAsync(seller.Id,storeId,request.Name,request.PriceIRR,ct); return Results.Ok(new{id});
}).RequirePermission("Seller.Catalog.Manage");

app.MapPost("/api/sellers/me/products/{productId:long}/warranties/{warrantyId:long}",async(System.Security.Claims.ClaimsPrincipal user,long productId,long warrantyId,LinkWarrantyRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException(); await service.LinkWarrantyAsync(seller.Id,productId,warrantyId,request.IsDefault,ct); return Results.NoContent();
}).RequirePermission("Seller.Catalog.Manage");


app.MapGet("/api/orders",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Orders.OrderQueryService service,CancellationToken ct)=>Results.Ok(await service.GetCustomerOrdersAsync(CurrentUserId(user),ct))).RequirePermission("Order.ReadOwn");
app.MapGet("/api/orders/{orderId:long}",async(System.Security.Claims.ClaimsPrincipal user,long orderId,Marketplace.Application.Orders.OrderQueryService service,CancellationToken ct)=>Results.Ok(await service.GetCustomerOrderAsync(CurrentUserId(user),orderId,ct))).RequirePermission("Order.ReadOwn");
app.MapGet("/api/seller/orders",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Orders.OrderQueryService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();return Results.Ok(await service.GetSellerOrdersAsync(seller.Id,ct));}).RequirePermission("Order.ReadOwn");
app.MapGet("/api/seller/orders/{orderId:long}",async(System.Security.Claims.ClaimsPrincipal user,long orderId,Marketplace.Application.Orders.OrderQueryService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();return Results.Ok(await service.GetSellerOrderAsync(seller.Id,orderId,ct));}).RequirePermission("Order.ReadOwn");
app.Run();

public sealed record CartItemRequest(long CustomerId,long SellerId,long StoreId,long ProductId,long VariantId,int Quantity,long? WarrantyId);
public sealed record CheckoutRequest(long CustomerId,Marketplace.Domain.Payments.PaymentProviderCode Provider,long DestinationCityId,string? CouponCode);
public sealed record StoreShippingCitiesRequest(long[] CityIds);
public sealed record SettlementRequest(long BankAccountId,long AmountIRR);
public sealed record SettlementReconciliationRequest(bool TransferCompleted,string? BankReference,string Note);
public sealed record PaymentProviderConfigureRequest(bool IsEnabled,bool IsVisible,int SortOrder,string ConfigurationJson);
public sealed record SmsProviderConfigureRequest(bool IsEnabled,bool IsVisible,int SortOrder);
public sealed record OtpRequest(string Mobile);
public sealed record OtpVerifyRequest(string Mobile, string Otp);
public sealed record PaymentVerifyRequest(string Authority);
public sealed record DeliveryConfirmRequest(string Code,string Reference,DateTime DeliveredAtUtc,DateTime ComplaintExpiresAtUtc);
public sealed record ComplaintRequest(long CustomerId,string Reason);
public sealed record ComplaintResolveRequest(bool CustomerWon,string Note);
public sealed record RefundRequest(Marketplace.Domain.Refunds.RefundReason Reason);
public sealed record CatalogProductRequest(long CategoryId,string Name,string Slug,string? Description,long BasePriceIRR,bool HasVariants);
public sealed record CatalogProductUpdateRequest(string Name,string Slug,string? Description,long BasePriceIRR);
public sealed record CatalogVariantRequest(string SKU,string VariantKey,long? PriceIRR,bool IsActive=true);
public sealed record StockRequest(long Quantity);
public sealed record WarrantyRequest(string Name,long PriceIRR);
public sealed record LinkWarrantyRequest(bool IsDefault);
public sealed record AttributeRequest(string Name,string Slug);
public sealed record AttributeValueRequest(string Value,string Slug);
public sealed record CategoryRequest(string Name,string Slug,long? ParentCategoryId);
public sealed record CommissionConfigRequest(int RateBasisPoints,long MinimumCommissionIRR);

public sealed class MarketplaceMaintenanceHostedService(IServiceScopeFactory scopes,ILogger<MarketplaceMaintenanceHostedService> logger):BackgroundService
{
 protected override async Task ExecuteAsync(CancellationToken stoppingToken)
 {
  await Run(stoppingToken);
  using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1));
  while(await timer.WaitForNextTickAsync(stoppingToken)) await Run(stoppingToken);
 }
 private async Task Run(CancellationToken ct)
 {
  try{using var scope=scopes.CreateScope();var service=scope.ServiceProvider.GetRequiredService<Marketplace.Application.Maintenance.MaintenanceService>();await service.RunOnceAsync(ct);}
  catch(OperationCanceledException) when(ct.IsCancellationRequested){}
  catch(Exception ex){logger.LogError(ex,"Marketplace maintenance cycle failed.");}
 }
}