using Marketplace.Application;
using Marketplace.Infrastructure;
using Marketplace.Api.Auth;
using Marketplace.Api.DTOs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;

var builder=WebApplication.CreateBuilder(args);

var jwtKey=builder.Configuration["Authentication:Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("Authentication:Jwt:Key must be configured with at least 32 characters.");

var jwtIssuer=builder.Configuration["Authentication:Jwt:Issuer"] ?? "Marketplace";
var jwtAudience=builder.Configuration["Authentication:Jwt:Audience"] ?? "Marketplace.Client";

builder.Services.AddMarketplaceApplication();
builder.Services.AddMarketplaceInfrastructure(builder.Configuration);
builder.Services.AddMemoryCache();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("otp-request", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("otp-verify", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 8,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(5),
                QueueLimit = 0,
                AutoReplenishment = true
            }));
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Marketplace.Api.Auth.OtpAuthService>();
builder.Services.AddScoped<Marketplace.Application.Abstractions.ISmsProviderSettings, Marketplace.Infrastructure.Notifications.SmsProviderSettingsRepository>();
builder.Services.AddScoped<Marketplace.Application.Abstractions.ISmsProviderSettingsAdmin, Marketplace.Infrastructure.Notifications.SmsProviderSettingsAdminRepository>();
builder.Services.AddScoped<Marketplace.Application.Notifications.SmsProviderSettingsService>();
builder.Services.AddScoped<Marketplace.Infrastructure.Notifications.LowStockSmsService>();
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
builder.Services.AddHostedService<Marketplace.Infrastructure.Outbox.OutboxRetentionHostedService>();
var app=builder.Build();
if (app.Environment.IsDevelopment()) app.UseSwagger().UseSwaggerUI();
app.UseExceptionHandler(errorApp=>errorApp.Run(async context=>{var feature=context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();var ex=feature?.Error;var status=ex is Marketplace.Domain.Common.DomainException?StatusCodes.Status400BadRequest:StatusCodes.Status500InternalServerError;context.Response.StatusCode=status;context.Response.ContentType="application/problem+json";await context.Response.WriteAsJsonAsync(new{title=status==400?"Validation error":"Server error",detail=status==400?ex?.Message:"An unexpected error occurred."});}));

long CurrentUserId(System.Security.Claims.ClaimsPrincipal user)
    => long.TryParse(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : throw new UnauthorizedAccessException();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapGet("/api/notifications",async(System.Security.Claims.ClaimsPrincipal user,int? take,Marketplace.Application.Notifications.NotificationService service,CancellationToken ct)=>Results.Ok(await service.GetAsync(CurrentUserId(user),take??50,ct))).RequireAuthorization();
app.MapGet("/api/notifications/unread-count",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Notifications.NotificationService service,CancellationToken ct)=>Results.Ok(new { count=await service.GetUnreadCountAsync(CurrentUserId(user),ct) })).RequireAuthorization();
app.MapPost("/api/notifications/read-all",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Notifications.NotificationService service,CancellationToken ct)=>Results.Ok(new { updated=await service.MarkAllReadAsync(CurrentUserId(user),ct) })).RequireAuthorization();
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
    Results.Ok(await otp.RequestAsync(request.Mobile, ct))).RequireRateLimiting("otp-request");

app.MapPost("/api/auth/otp/verify", async (OtpVerifyRequest request, Marketplace.Api.Auth.OtpAuthService otp, CancellationToken ct) =>
    Results.Ok(await otp.VerifyAsync(request.Mobile, request.Otp, ct))).RequireRateLimiting("otp-verify");

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
}).RequireRateLimiting("login");

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
            product.Name, variant.SKU, variant.VariantKey, StoreId = store.Id, StoreSlug = store.Slug, StoreName = store.Name, StoreThemeCode = store.ThemeCode, StorePaletteCode = store.PaletteCode, StoreThemePrimaryColor = store.ThemePrimaryColor, StoreThemeSecondaryColor = store.ThemeSecondaryColor, StoreThemeBackgroundColor = store.ThemeBackgroundColor, StoreThemeTextColor = store.ThemeTextColor, StoreThemeFontCode = store.ThemeFontCode, StoreThemeCornerStyle = store.ThemeCornerStyle,
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

app.MapPut("/api/cart/items/{variantId:long}",async(System.Security.Claims.ClaimsPrincipal user,long variantId,CartQuantityRequest request,Marketplace.Application.Cart.CartService service,CancellationToken ct)=>{
    await service.SetQuantityAsync(CurrentUserId(user),variantId,request.WarrantyId,request.Quantity,ct);
    return Results.NoContent();
}).RequirePermission("Cart.Read");

app.MapDelete("/api/cart/items/{variantId:long}",async(System.Security.Claims.ClaimsPrincipal user,long variantId,long? warrantyId,Marketplace.Application.Cart.CartService service,CancellationToken ct)=>{
    await service.RemoveItemAsync(CurrentUserId(user),variantId,warrantyId,ct); return Results.Ok();
}).RequirePermission("Cart.Read");


app.MapPost("/api/sellers/me/stores/{storeId:long}/attributes",async(System.Security.Claims.ClaimsPrincipal user,long storeId,AttributeRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();var id=await service.CreateAttributeAsync(seller.Id,storeId,request.Name,request.Slug,ct);return Results.Ok(new{id});}).RequirePermission("Seller.Catalog.Manage");
app.MapPost("/api/sellers/me/attributes/{attributeId:long}/values",async(System.Security.Claims.ClaimsPrincipal user,long attributeId,AttributeValueRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();var id=await service.CreateAttributeValueAsync(seller.Id,attributeId,request.Value,request.Slug,ct);return Results.Ok(new{id});}).RequirePermission("Seller.Catalog.Manage");
app.MapPost("/api/sellers/me/products/{productId:long}/attributes/{attributeId:long}",async(System.Security.Claims.ClaimsPrincipal user,long productId,long attributeId,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();await service.AssignAttributeAsync(seller.Id,productId,attributeId,ct);return Results.NoContent();}).RequirePermission("Seller.Catalog.Manage");
app.MapPost("/api/sellers/me/variants/{variantId:long}/attribute-values/{attributeValueId:long}",async(System.Security.Claims.ClaimsPrincipal user,long variantId,long attributeValueId,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();await service.AssignVariantValueAsync(seller.Id,variantId,attributeValueId,ct);return Results.NoContent();}).RequirePermission("Seller.Catalog.Manage");
app.MapGet("/api/categories",async(long? parentId,Marketplace.Application.Catalog.CategoryManagementService service,CancellationToken ct)=>Results.Ok(await service.GetChildrenAsync(parentId,ct)));
app.MapGet("/api/public/stores", async (int? take, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var limit = Math.Clamp(take ?? 60, 1, 100);
    var stores = await (
        from s in db.Stores.AsNoTracking()
        join seller in db.Sellers.AsNoTracking() on s.SellerId equals seller.Id
        where s.Status == Marketplace.Domain.Sellers.StoreStatus.Active
              && seller.Status == Marketplace.Domain.Sellers.SellerStatus.Active
        orderby s.CreatedAtUtc descending
        select new
        {
            s.Id, s.Name, s.Slug, s.Description, s.ThemeCode, s.PaletteCode, s.ThemePrimaryColor, s.ThemeSecondaryColor, s.ThemeBackgroundColor, s.ThemeTextColor, s.ThemeFontCode, s.ThemeCornerStyle, s.CreatedAtUtc,
            ProductCount = db.Products.Count(p => p.StoreId == s.Id && p.Status == Marketplace.Domain.Catalog.ProductStatus.Active)
        }).Take(limit).ToListAsync(ct);
    return Results.Ok(stores);
});

app.MapGet("/api/public/stores/{storeId:long}/{slug}", async (long storeId, string slug, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var store = await (
        from s in db.Stores.AsNoTracking()
        join seller in db.Sellers.AsNoTracking() on s.SellerId equals seller.Id
        where s.Id == storeId && s.Slug == slug
              && s.Status == Marketplace.Domain.Sellers.StoreStatus.Active
              && seller.Status == Marketplace.Domain.Sellers.SellerStatus.Active
        select new { s.Id, s.Name, s.Slug, s.Description, s.ThemeCode, s.PaletteCode, s.ThemePrimaryColor, s.ThemeSecondaryColor, s.ThemeBackgroundColor, s.ThemeTextColor, s.ThemeFontCode, s.ThemeCornerStyle, s.CreatedAtUtc }
    ).SingleOrDefaultAsync(ct);
    return store is null ? Results.NotFound() : Results.Ok(store);
});

app.MapGet("/api/catalog/products",async(string? q,long? categoryId,long? storeId,int? take,int? skip,Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,CancellationToken ct)=>{
    var limit=Math.Clamp(take??60,1,100);
    var offset=Math.Max(skip??0,0);
    var query=from p in db.Products.AsNoTracking()
              join s in db.Stores.AsNoTracking() on p.StoreId equals s.Id
              join seller in db.Sellers.AsNoTracking() on s.SellerId equals seller.Id
              join c in db.Categories.AsNoTracking() on p.CategoryId equals c.Id
              where p.Status==Marketplace.Domain.Catalog.ProductStatus.Active
                    && s.Status==Marketplace.Domain.Sellers.StoreStatus.Active
                    && seller.Status==Marketplace.Domain.Sellers.SellerStatus.Active && c.IsActive
                    && (!categoryId.HasValue||p.CategoryId==categoryId.Value)
                    && (!storeId.HasValue||p.StoreId==storeId.Value)
                    && (string.IsNullOrWhiteSpace(q)||p.Name.Contains(q)||p.Description!.Contains(q)||s.Name.Contains(q))
              orderby p.CreatedAtUtc descending
              select new { p.Id,p.StoreId,SellerId=s.SellerId,StoreName=s.Name,StoreSlug=s.Slug,p.CategoryId,CategoryName=c.Name,p.Name,p.Slug,p.Description,p.BasePriceIRR,p.HasVariants };
    var products=await query.Skip(offset).Take(limit).ToListAsync(ct);
    var ids=products.Select(x=>x.Id).ToArray();
    var variants=await (from v in db.ProductVariants.AsNoTracking()
        join inv0 in db.InventoryItems.AsNoTracking().Where(x=>x.IsActive) on v.Id equals inv0.ProductVariantId into invs
        from inv in invs.DefaultIfEmpty()
        where ids.Contains(v.ProductId)&&v.IsActive
        select new { v.Id,v.ProductId,v.SKU,v.VariantKey,PriceIRR=v.PriceIRR,AvailableQuantity=inv==null?0L:inv.StockQuantity-inv.ReservedQuantity })
        .ToListAsync(ct);
    var warranties=await (from pw in db.ProductWarranties.AsNoTracking()
        join w in db.Warranties.AsNoTracking() on pw.WarrantyId equals w.Id
        where ids.Contains(pw.ProductId)&&pw.IsActive&&w.IsActive
        select new { pw.ProductId,WarrantyId=w.Id,w.Name,w.PriceIRR,pw.IsDefault })
        .ToListAsync(ct);
    return Results.Ok(products.Select(p=>new {
        p.Id,p.StoreId,p.SellerId,p.StoreName,p.StoreSlug,p.CategoryId,p.CategoryName,p.Name,p.Slug,p.Description,p.BasePriceIRR,p.HasVariants,
        Variants=variants.Where(v=>v.ProductId==p.Id).Select(v=>new {v.Id,v.SKU,v.VariantKey,PriceIRR=v.PriceIRR??p.BasePriceIRR,v.AvailableQuantity}),
        Warranties=warranties.Where(w=>w.ProductId==p.Id).Select(w=>new {id=w.WarrantyId,w.Name,w.PriceIRR,w.IsDefault})
    }));
});

app.MapGet("/api/public/stores/{storeId:long}/{storeSlug}/products/{productSlug}", async (long storeId, string storeSlug, string productSlug, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var product = await (
        from p in db.Products.AsNoTracking()
        join s in db.Stores.AsNoTracking() on p.StoreId equals s.Id
        join seller in db.Sellers.AsNoTracking() on s.SellerId equals seller.Id
        join category in db.Categories.AsNoTracking() on p.CategoryId equals category.Id
        where p.StoreId == storeId && s.Slug == storeSlug && p.Slug == productSlug
              && p.Status == Marketplace.Domain.Catalog.ProductStatus.Active
              && s.Status == Marketplace.Domain.Sellers.StoreStatus.Active
              && seller.Status == Marketplace.Domain.Sellers.SellerStatus.Active && category.IsActive
        select new
        {
            p.Id, p.StoreId, SellerId = s.SellerId, StoreName = s.Name, StoreSlug = s.Slug, ThemeCode = s.ThemeCode, PaletteCode = s.PaletteCode, ThemePrimaryColor = s.ThemePrimaryColor, ThemeSecondaryColor = s.ThemeSecondaryColor, ThemeBackgroundColor = s.ThemeBackgroundColor, ThemeTextColor = s.ThemeTextColor, ThemeFontCode = s.ThemeFontCode, ThemeCornerStyle = s.ThemeCornerStyle,
            p.CategoryId, CategoryName = category.Name, p.Name, p.Slug, p.Description,
            p.BasePriceIRR, p.HasVariants, p.CreatedAtUtc
        }).SingleOrDefaultAsync(ct);
    if (product is null) return Results.NotFound();

    var variants = await (
        from v in db.ProductVariants.AsNoTracking()
        join inv0 in db.InventoryItems.AsNoTracking().Where(x => x.IsActive) on v.Id equals inv0.ProductVariantId into invs
        from inv in invs.DefaultIfEmpty()
        where v.ProductId == product.Id && v.IsActive
        orderby v.Id
        select new { v.Id, v.SKU, v.VariantKey, PriceIRR = v.PriceIRR ?? product.BasePriceIRR,
            AvailableQuantity = inv == null ? 0L : inv.StockQuantity - inv.ReservedQuantity }
    ).ToListAsync(ct);
    var variantIds = variants.Select(v => v.Id).ToArray();
    var attributes = await (
        from link in db.VariantAttributeValues.AsNoTracking()
        join value in db.ProductAttributeValues.AsNoTracking() on link.ProductAttributeValueId equals value.Id
        join attribute in db.ProductAttributes.AsNoTracking() on value.ProductAttributeId equals attribute.Id
        where variantIds.Contains(link.ProductVariantId) && value.IsActive && attribute.IsActive
        select new { link.ProductVariantId, AttributeName = attribute.Name, value.Value }
    ).ToListAsync(ct);
    var warranties = await (
        from pw in db.ProductWarranties.AsNoTracking()
        join w in db.Warranties.AsNoTracking() on pw.WarrantyId equals w.Id
        where pw.ProductId == product.Id && pw.IsActive && w.IsActive
        select new { Id = w.Id, w.Name, w.PriceIRR, pw.IsDefault }
    ).ToListAsync(ct);

    return Results.Ok(new
    {
        product.Id, product.StoreId, product.SellerId, product.StoreName, product.StoreSlug,
        product.CategoryId, product.CategoryName, product.Name, product.Slug, product.Description,
        product.BasePriceIRR, product.HasVariants, product.CreatedAtUtc,
        Variants = variants.Select(v => new
        {
            v.Id, v.SKU, v.VariantKey, v.PriceIRR, v.AvailableQuantity,
            Attributes = attributes.Where(a => a.ProductVariantId == v.Id)
                .Select(a => new { a.AttributeName, a.Value })
        }),
        Warranties = warranties
    });
});

app.MapPost("/api/admin/categories",async(CategoryRequest request,Marketplace.Application.Catalog.CategoryManagementService service,CancellationToken ct)=>Results.Ok(new{id=await service.CreateAsync(request.Name,request.Slug,request.ParentCategoryId,ct)})).RequirePermission("Admin.Identity.Manage");
app.MapPut("/api/admin/categories/{categoryId:long}",async(long categoryId,CategoryRequest request,Marketplace.Application.Catalog.CategoryManagementService service,CancellationToken ct)=>{await service.RenameAsync(categoryId,request.Name,request.Slug,ct);return Results.NoContent();}).RequirePermission("Admin.Identity.Manage");
app.MapPost("/api/admin/categories/{categoryId:long}/deactivate",async(long categoryId,Marketplace.Application.Catalog.CategoryManagementService service,CancellationToken ct)=>{await service.DeactivateAsync(categoryId,ct);return Results.NoContent();}).RequirePermission("Admin.Identity.Manage");
app.MapGet("/api/shipping/cities",async(Marketplace.Application.Shipping.ShippingCoverageService service,CancellationToken ct)=>
    Results.Ok(await service.GetCitiesAsync(ct)));

app.MapGet("/api/cart/shipping-options", async (
    System.Security.Claims.ClaimsPrincipal user, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    var cart = await db.Carts.AsNoTracking().SingleOrDefaultAsync(x => x.CustomerId == customerId, ct);
    if (cart is null) return Results.Ok(Array.Empty<object>());
    var cities = await db.StoreShippingCities.AsNoTracking()
        .Where(x => x.StoreId == cart.StoreId && x.City.IsActive)
        .Join(db.StoreShippingRates.AsNoTracking(), coverage => new { coverage.StoreId, coverage.CityId }, rate => new { rate.StoreId, rate.CityId }, (coverage, rate) => new { coverage.City, rate.ShippingFeeIRR, rate.MinDeliveryDays, rate.MaxDeliveryDays })
        .OrderBy(x => x.City.ProvinceName).ThenBy(x => x.City.Name)
        .Select(x => new { id = x.City.Id, name = x.City.Name, provinceName = x.City.ProvinceName, x.ShippingFeeIRR, x.MinDeliveryDays, x.MaxDeliveryDays })
        .ToListAsync(ct);
    return Results.Ok(cities);
}).RequirePermission("Order.Create");


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

app.MapPut("/api/sellers/me/stores/{storeId:long}/theme", async (
    System.Security.Claims.ClaimsPrincipal user, long storeId, StoreThemeRequest request,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    Marketplace.Application.Abstractions.ISellerManagementRepository sellers, CancellationToken ct) =>
{
    var seller = await sellers.GetSellerByUserIdAsync(CurrentUserId(user), ct);
    if (seller is null) return Results.Forbid();
    var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == storeId && x.SellerId == seller.Id, ct);
    if (store is null) return Results.NotFound();
    store.ConfigureTheme(request.ThemeCode);
    store.ConfigurePalette(request.PaletteCode);
    store.ConfigureAppearance(request.PrimaryColor, request.SecondaryColor, request.BackgroundColor,
        request.TextColor, request.FontCode, request.CornerStyle);
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { store.Id, store.ThemeCode, store.PaletteCode, store.ThemePrimaryColor,
        store.ThemeSecondaryColor, store.ThemeBackgroundColor, store.ThemeTextColor, store.ThemeFontCode, store.ThemeCornerStyle });
}).RequirePermission("Seller.Catalog.Manage");

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

app.MapGet("/api/stores/{storeId:long}/shipping-rates", async (
    System.Security.Claims.ClaimsPrincipal user, long storeId, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    Marketplace.Application.Abstractions.ISellerManagementRepository sellers, CancellationToken ct) =>
{
    var seller = await sellers.GetSellerByUserIdAsync(CurrentUserId(user), ct) ?? throw new UnauthorizedAccessException();
    if (!await sellers.StoreBelongsToSellerAsync(storeId, seller.Id, ct))
        throw new Marketplace.Domain.Common.DomainException("Store not found.");
    return Results.Ok(await db.StoreShippingRates.AsNoTracking()
        .Where(x => x.StoreId == storeId)
        .Join(db.DeliveryCities.AsNoTracking(), rate => rate.CityId, city => city.Id, (rate, city) => new
        {
            cityId = city.Id, cityName = city.Name, provinceName = city.ProvinceName,
            rate.ShippingFeeIRR, rate.MinDeliveryDays, rate.MaxDeliveryDays, rate.UpdatedAtUtc
        }).OrderBy(x => x.provinceName).ThenBy(x => x.cityName).ToListAsync(ct));
}).RequirePermission("Seller.Shipping.Configure");

app.MapPut("/api/stores/{storeId:long}/shipping-rates", async (
    System.Security.Claims.ClaimsPrincipal user, long storeId, StoreShippingRatesRequest request,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, Marketplace.Application.Abstractions.ISellerManagementRepository sellers,
    Marketplace.Application.Abstractions.IIdGenerator ids, CancellationToken ct) =>
{
    var userId = CurrentUserId(user);
    var seller = await sellers.GetSellerByUserIdAsync(userId, ct) ?? throw new UnauthorizedAccessException();
    if (!await sellers.StoreBelongsToSellerAsync(storeId, seller.Id, ct))
        throw new Marketplace.Domain.Common.DomainException("Store not found.");
    var requested = request.Rates ?? Array.Empty<StoreShippingRateRequest>();
    if (requested.Any(x => x.CityId <= 0) || requested.Select(x => x.CityId).Distinct().Count() != requested.Length)
        throw new Marketplace.Domain.Common.DomainException("Shipping rates must contain unique valid city identifiers.");
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
    var coverage = await db.StoreShippingCities.Where(x => x.StoreId == storeId).Select(x => x.CityId).ToListAsync(ct);
    if (coverage.Count != requested.Length || !coverage.ToHashSet().SetEquals(requested.Select(x => x.CityId)))
        throw new Marketplace.Domain.Common.DomainException("Configure exactly one shipping rate for every city enabled in the store's shipping coverage.");
    var cityIds = requested.Select(x => x.CityId).ToArray();
    var activeCityCount = await db.DeliveryCities.CountAsync(x => cityIds.Contains(x.Id) && x.IsActive, ct);
    if (activeCityCount != cityIds.Length)
        throw new Marketplace.Domain.Common.DomainException("All shipping rate destinations must be active cities.");
    var existing = await db.StoreShippingRates.Where(x => x.StoreId == storeId).ToListAsync(ct);
    var now = DateTime.UtcNow;
    foreach (var item in requested)
    {
        var rate = existing.SingleOrDefault(x => x.CityId == item.CityId);
        if (rate is null)
            db.StoreShippingRates.Add(Marketplace.Domain.Shipping.StoreShippingRate.Create(await ids.NextAsync(ct), storeId, item.CityId, item.ShippingFeeIRR, item.MinDeliveryDays, item.MaxDeliveryDays, now));
        else
            rate.Update(item.ShippingFeeIRR, item.MinDeliveryDays, item.MaxDeliveryDays, now);
    }
    db.StoreShippingRates.RemoveRange(existing.Where(x => !cityIds.Contains(x.CityId)));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return Results.NoContent();
}).RequirePermission("Seller.Shipping.Configure");


app.MapGet("/api/payments/providers",async(Marketplace.Application.Abstractions.IPaymentProviderSettings settings,CancellationToken ct)=>
    Results.Ok(await settings.GetAvailableAsync(ct)));

app.MapGet("/api/admin/sms-providers",async(Marketplace.Application.Notifications.SmsProviderSettingsService service,CancellationToken ct)=>
    Results.Ok(await service.GetAllAsync(ct))).RequirePermission("Admin.SmsProviders.Read");
app.MapPut("/api/admin/sms-providers/{provider}",async(string provider,SmsProviderConfigureRequest request,Marketplace.Application.Notifications.SmsProviderSettingsService service,CancellationToken ct)=>{
    await service.ConfigureAsync(provider,request.IsEnabled,request.IsVisible,request.SortOrder,ct);
    return Results.NoContent();
}).RequirePermission("Admin.SmsProviders.Configure");

app.MapGet("/api/admin/sms-automation",async(Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,CancellationToken ct)=>{
    var setting=await db.SmsAutomationSettings.SingleOrDefaultAsync(x=>x.Id==1,ct);
    if(setting is null){setting=Marketplace.Domain.Notifications.SmsAutomationSetting.CreateDefault();db.SmsAutomationSettings.Add(setting);await db.SaveChangesAsync(ct);}
    return Results.Ok(new { setting.AutomaticSmsEnabled, setting.LowStockSmsEnabled, setting.UpdatedAtUtc });
}).RequirePermission("Admin.SmsProviders.Read");
app.MapPut("/api/admin/sms-automation",async(SmsAutomationConfigureRequest request,Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,CancellationToken ct)=>{
    var setting=await db.SmsAutomationSettings.SingleOrDefaultAsync(x=>x.Id==1,ct);
    if(setting is null){setting=Marketplace.Domain.Notifications.SmsAutomationSetting.CreateDefault();db.SmsAutomationSettings.Add(setting);}
    setting.Configure(request.AutomaticSmsEnabled,request.LowStockSmsEnabled);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequirePermission("Admin.SmsProviders.Configure");

app.MapGet("/api/admin/payment-providers",async(Marketplace.Application.Payments.PaymentProviderSettingsService service,CancellationToken ct)=>
    Results.Ok(await service.GetAllAsync(ct))).RequirePermission("Admin.PaymentProviders.Read");

app.MapGet("/api/admin/audit-events",async(int? take,Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,CancellationToken ct)=>
    Results.Ok(await db.AdminAuditEvents.AsNoTracking().OrderByDescending(x=>x.CreatedAtUtc)
        .Take(Math.Clamp(take??100,1,200))
        .Select(x=>new { id=x.Id,actorUserId=x.ActorUserId,action=x.Action,entityType=x.EntityType,entityKey=x.EntityKey,detailsJson=x.DetailsJson,correlationId=x.CorrelationId,createdAtUtc=x.CreatedAtUtc })
        .ToListAsync(ct))).RequirePermission("Admin.PaymentProviders.Read");

app.MapPut("/api/admin/payment-providers/{provider}",async(Marketplace.Domain.Payments.PaymentProviderCode provider,PaymentProviderConfigureRequest request,System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Payments.PaymentProviderSettingsService service,CancellationToken ct)=>{
    await service.ConfigureAsync(provider,request.IsEnabled,request.IsVisible,request.SortOrder,request.ConfigurationJson,ct,CurrentUserId(user));
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
    var result=await service.RequestAsync(CurrentUserId(user),request.BankAccountId,request.AmountIRR,request.RequestKey,ct); return Results.Ok(result);
}).RequirePermission("Seller.Settlement.Request");

app.MapPost("/api/settlements/{settlementId:long}/process", async (
    long settlementId,
    Marketplace.Application.Settlements.SettlementService service,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    try
    {
        var result = await service.ProcessAsync(settlementId, ct);
        return result.Status == "Completed"
            ? (IResult)Results.Ok(result)
            : Results.BadRequest(result);
    }
    catch (Exception exception)
    {
        // SettlementService deliberately moves ambiguous gateway/finalization outcomes to
        // OnHold and keeps the money reserved. Return that persisted state to the admin UI
        // instead of making an expected reconciliation workflow look like an opaque HTTP 500.
        try
        {
            var persisted = await db.Settlements.AsNoTracking()
                .Where(x => x.Id == settlementId)
                .Select(x => new { x.Id, x.AmountIRR, x.Status, x.Reference })
                .SingleOrDefaultAsync(CancellationToken.None);

            if (persisted?.Status == Marketplace.Domain.Finance.SettlementStatus.OnHold)
            {
                logger.LogWarning(exception,
                    "Settlement {SettlementId} is on hold after an ambiguous payout outcome and requires bank reconciliation.",
                    settlementId);

                return Results.Ok(new
                {
                    settlementId = persisted.Id,
                    amountIRR = persisted.AmountIRR,
                    status = persisted.Status.ToString(),
                    reference = persisted.Reference,
                    outcomeRequiresReconciliation = true,
                    message = "Payout outcome is ambiguous. Reconcile against the bank's final status before retrying."
                });
            }
        }
        catch (Exception lookupException)
        {
            logger.LogError(lookupException,
                "Could not read persisted settlement {SettlementId} after processing failed.",
                settlementId);
        }

        logger.LogError(exception, "Settlement {SettlementId} processing failed without a confirmed OnHold outcome.", settlementId);
        throw;
    }
}).RequirePermission("Admin.Settlement.Process");

app.MapPost("/api/admin/settlements/{settlementId:long}/reconcile", async (long settlementId, System.Security.Claims.ClaimsPrincipal user, SettlementReconciliationRequest request, Marketplace.Application.Settlements.SettlementService service, CancellationToken ct) =>
{
    var result = await service.ReconcileAsync(settlementId, CurrentUserId(user), request.TransferCompleted, request.BankReference, request.Note, ct);
    return Results.Ok(result);
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/settlements/reconciliation/history", async (Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var items = await (
        from audit in db.SettlementReconciliationAudits.AsNoTracking()
        join settlement in db.Settlements.AsNoTracking() on audit.SettlementId equals settlement.Id
        orderby audit.CreatedAtUtc descending
        select new
        {
            audit.Id,
            audit.SettlementId,
            settlement.SellerId,
            settlement.AmountIRR,
            audit.AdminUserId,
            audit.TransferCompleted,
            audit.Note,
            audit.BankReference,
            audit.CreatedAtUtc
        }).Take(200).ToListAsync(ct);
    return Results.Ok(items);
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/public/products/{productId:long}/reviews", async (long productId, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    if (!await db.Products.AsNoTracking().AnyAsync(x => x.Id == productId, ct)) return Results.NotFound();
    var summary = await db.ProductReviews.AsNoTracking()
        .Where(x => x.ProductId == productId && x.Status == Marketplace.Domain.Catalog.ProductReviewStatus.Approved)
        .GroupBy(x => x.ProductId)
        .Select(group => new { AverageRating = group.Average(x => (double)x.Rating), TotalReviews = group.Count() })
        .SingleOrDefaultAsync(ct);
    var items = await (from review in db.ProductReviews.AsNoTracking()
        join user in db.Users.AsNoTracking() on review.CustomerId equals user.Id
        where review.ProductId == productId && review.Status == Marketplace.Domain.Catalog.ProductReviewStatus.Approved
        orderby review.CreatedAtUtc descending
        select new { review.Id, review.Rating, review.Title, review.Body, reviewer = user.DisplayName, review.CreatedAtUtc })
        .Take(100).ToListAsync(ct);
    return Results.Ok(new { averageRating = summary is null ? 0 : Math.Round(summary.AverageRating, 1), totalReviews = summary?.TotalReviews ?? 0, items });
});

app.MapPost("/api/products/{productId:long}/reviews", async (System.Security.Claims.ClaimsPrincipal user, long productId, ProductReviewCreateRequest request, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, Marketplace.Application.Abstractions.IIdGenerator ids, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    if (!await db.Products.AsNoTracking().AnyAsync(x => x.Id == productId, ct)) return Results.NotFound(new { detail = "محصول پیدا نشد." });
    var eligible = await db.Orders.AsNoTracking().AnyAsync(order =>
        order.Id == request.OrderId && order.CustomerId == customerId &&
        (order.Status == Marketplace.Domain.Orders.OrderStatus.Delivered || order.Status == Marketplace.Domain.Orders.OrderStatus.Completed) &&
        db.OrderItems.Any(item => item.OrderId == order.Id && item.ProductId == productId), ct);
    if (!eligible) return Results.BadRequest(new { detail = "ثبت نظر فقط برای محصولی امکان‌پذیر است که در سفارش تحویل‌شده شما وجود داشته باشد." });
    if (await db.ProductReviews.AnyAsync(x => x.CustomerId == customerId && x.ProductId == productId, ct))
        return Results.Conflict(new { detail = "برای این محصول قبلاً نظر ثبت کرده‌اید." });
    var review = Marketplace.Domain.Catalog.ProductReview.Create(await ids.NextAsync(ct), productId, customerId, request.OrderId, request.Rating, request.Title, request.Body);
    db.ProductReviews.Add(review);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/products/{productId}/reviews", new { review.Id, status = review.Status.ToString(), review.CreatedAtUtc });
}).RequirePermission("Order.ReadOwn");

app.MapGet("/api/products/{productId:long}/reviewable-orders", async (System.Security.Claims.ClaimsPrincipal user, long productId, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    var orders = await (from order in db.Orders.AsNoTracking()
        join item in db.OrderItems.AsNoTracking() on order.Id equals item.OrderId
        where order.CustomerId == customerId && item.ProductId == productId &&
            (order.Status == Marketplace.Domain.Orders.OrderStatus.Delivered || order.Status == Marketplace.Domain.Orders.OrderStatus.Completed) &&
            !db.ProductReviews.Any(review => review.CustomerId == customerId && review.ProductId == productId)
        orderby order.DeliveredAtUtc descending, order.CreatedAtUtc descending
        select new { orderId = order.Id, order.CreatedAtUtc, order.DeliveredAtUtc })
        .Distinct().Take(20).ToListAsync(ct);
    return Results.Ok(orders);
}).RequirePermission("Order.ReadOwn");

app.MapGet("/api/admin/product-reviews", async (int? status, int? take, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var query = from review in db.ProductReviews.AsNoTracking()
        join user in db.Users.AsNoTracking() on review.CustomerId equals user.Id
        join product in db.Products.AsNoTracking() on review.ProductId equals product.Id
        select new { review.Id, review.ProductId, productName = product.Name, review.CustomerId, customerName = user.DisplayName, review.OrderId, review.Rating, review.Title, review.Body, review.Status, review.CreatedAtUtc, review.ModeratedAtUtc, review.ModeratorUserId, review.ModerationNote };
    if (status.HasValue && status.Value is >= 1 and <= 3) query = query.Where(x => (int)x.Status == status.Value);
    return Results.Ok(await query.OrderBy(x => x.Status).ThenByDescending(x => x.CreatedAtUtc).Take(Math.Clamp(take ?? 100, 1, 200)).ToListAsync(ct));
}).RequirePermission("Admin.Order.Read");

app.MapPut("/api/admin/product-reviews/{reviewId:long}/moderation", async (System.Security.Claims.ClaimsPrincipal user, long reviewId, ProductReviewModerationRequest request, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var review = await db.ProductReviews.SingleOrDefaultAsync(x => x.Id == reviewId, ct);
    if (review is null) return Results.NotFound();
    var moderatorId = CurrentUserId(user);
    if (request.Approve) review.Approve(moderatorId);
    else review.Reject(moderatorId, request.Note ?? string.Empty);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequirePermission("Admin.Order.Read");

app.MapGet("/api/orders/checkout/quote",async(System.Security.Claims.ClaimsPrincipal user,long destinationCityId,string? couponCode,Marketplace.Application.Orders.OrderCreationService service,CancellationToken ct)=>
    Results.Ok(await service.QuoteAsync(CurrentUserId(user),destinationCityId,couponCode,ct)))
    .RequirePermission("Order.Create");

app.MapPost("/api/orders/checkout",async(System.Security.Claims.ClaimsPrincipal user,CheckoutRequest request,Marketplace.Application.Orders.OrderCreationService service,Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,Marketplace.Infrastructure.Notifications.LowStockSmsService lowStockSms,CancellationToken ct)=>{
    if(string.IsNullOrWhiteSpace(request.RequestKey)||request.RequestKey.Length<16||request.RequestKey.Length>64) return Results.BadRequest(new { detail="A valid checkout request key is required." });
    var customerId = CurrentUserId(user);
    var existingRequest = await db.Orders.AsNoTracking()
        .AnyAsync(x => x.CustomerId == customerId && x.RequestKey == request.RequestKey, ct);
    Marketplace.Application.Orders.CustomerAddressSnapshot? snapshot = null;
    if (!existingRequest)
    {
        if (!request.AddressId.HasValue || request.AddressId.Value <= 0)
            return Results.BadRequest(new { detail = "پیش از ثبت سفارش، نشانی تحویل را انتخاب کنید." });
        var address = await db.CustomerAddresses.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == request.AddressId.Value && x.CustomerId == customerId, ct);
        if (address is null)
            return Results.BadRequest(new { detail = "نشانی انتخاب‌شده متعلق به حساب شما نیست یا حذف شده است." });
        if (address.CityId != request.DestinationCityId)
            return Results.BadRequest(new { detail = "شهر نشانی با شهر انتخاب‌شده برای ارسال یکسان نیست." });
        snapshot = new Marketplace.Application.Orders.CustomerAddressSnapshot(
            address.RecipientName, address.RecipientMobile, address.AddressLine, address.PostalCode, address.DeliveryNote);
    }
    var result=await service.CheckoutAsync(customerId,request.Provider,request.DestinationCityId,request.CouponCode,request.RequestKey,ct,snapshot);
    var variants=await db.OrderItems.AsNoTracking().Where(x=>x.OrderId==result.OrderId&&x.VariantId.HasValue).Select(x=>x.VariantId!.Value).Distinct().ToListAsync(ct);
    foreach(var variantId in variants) await lowStockSms.NotifyIfLowAsync(variantId,ct);
    return Results.Ok(result);
}).RequirePermission("Order.Create");

app.MapGet("/api/payments/test-return", async (long paymentId, string authority, string? result,
    Marketplace.Application.Orders.PaymentVerificationService service,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    IConfiguration configuration, CancellationToken ct) =>
{
    var verified = await service.VerifyTestReturnAsync(paymentId, authority,
        string.Equals(result, "success", StringComparison.OrdinalIgnoreCase), ct);
    var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(x => x.Id == paymentId, ct);
    var returnBase = configuration["Payment:ClientReturnBaseUrl"];
    if (payment is not null && Uri.TryCreate(returnBase, UriKind.Absolute, out var clientBase)
        && (clientBase.Scheme == Uri.UriSchemeHttps || clientBase.Scheme == Uri.UriSchemeHttp))
    {
        var separator = returnBase!.Contains('?') ? "&" : "?";
        var target = $"{returnBase.TrimEnd('/')}/payment-result?orderId={payment.OrderId}&status={(verified.Paid ? "success" : "failed")}";
        return Results.Redirect(target);
    }

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


app.MapPost("/api/seller/orders/{orderId:long}/shipment", async (
    System.Security.Claims.ClaimsPrincipal user, long orderId, ShipmentCreateRequest request,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, Marketplace.Application.Abstractions.IIdGenerator ids,
    CancellationToken ct) =>
{
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
    var sellerUserId = CurrentUserId(user);
    var seller = await db.Sellers.SingleOrDefaultAsync(x => x.UserId == sellerUserId, ct) ?? throw new UnauthorizedAccessException();
    var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct) ?? throw new Marketplace.Domain.Common.DomainException("Order not found.");
    if (order.SellerId != seller.Id) throw new Marketplace.Domain.Common.DomainException("You do not own this order.");
    if (order.Status is not (Marketplace.Domain.Orders.OrderStatus.Paid or Marketplace.Domain.Orders.OrderStatus.Preparing or Marketplace.Domain.Orders.OrderStatus.ReadyForDelivery))
        throw new Marketplace.Domain.Common.DomainException("A shipment can only be registered for a paid order that has not been delivered or cancelled.");
    if (await db.Shipments.AnyAsync(x => x.OrderId == orderId, ct))
        return Results.Conflict(new { detail = "A shipment is already registered for this order. Update its tracking status instead." });
    var now = DateTime.UtcNow;
    var shipment = Marketplace.Domain.Shipping.Shipment.Create(await ids.NextAsync(ct), order.Id, seller.Id, request.CarrierName, request.TrackingNumber, request.TrackingUrl, now);
    var trackingEvent = Marketplace.Domain.Shipping.ShipmentTrackingEvent.Create(await ids.NextAsync(ct), shipment.Id, shipment.Status, "اطلاعات مرسوله ثبت شد.", null, sellerUserId, now);
    var notification = Marketplace.Domain.Notifications.Notification.Create(await ids.NextAsync(ct), order.CustomerId, Marketplace.Domain.Notifications.NotificationChannel.InApp, "مرسوله سفارش ثبت شد", $"اطلاعات ارسال سفارش شماره {order.Id} ثبت شد. کد رهگیری: {shipment.TrackingNumber}", "Order", order.Id);
    notification.MarkSent();
    db.Shipments.Add(shipment);
    db.ShipmentTrackingEvents.Add(trackingEvent);
    db.Notifications.Add(notification);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return Results.Created($"/api/orders/{order.Id}/shipment", new { shipment.Id, shipment.OrderId, shipment.CarrierName, shipment.TrackingNumber, shipment.TrackingUrl, status = shipment.Status.ToString(), shipment.CreatedAtUtc, events = new[] { new { trackingEvent.Status, trackingEvent.Description, trackingEvent.Location, trackingEvent.OccurredAtUtc } } });
}).RequirePermission("Order.Delivery.Confirm");

app.MapPost("/api/seller/orders/{orderId:long}/shipment/status", async (
    System.Security.Claims.ClaimsPrincipal user, long orderId, ShipmentStatusUpdateRequest request,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, Marketplace.Application.Abstractions.IIdGenerator ids,
    CancellationToken ct) =>
{
    if (!Enum.IsDefined(request.Status)) throw new Marketplace.Domain.Common.DomainException("Invalid shipment status.");
    if (string.IsNullOrWhiteSpace(request.Description)) throw new Marketplace.Domain.Common.DomainException("Tracking description is required.");
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
    var sellerUserId = CurrentUserId(user);
    var seller = await db.Sellers.SingleOrDefaultAsync(x => x.UserId == sellerUserId, ct) ?? throw new UnauthorizedAccessException();
    var order = await db.Orders.SingleOrDefaultAsync(x => x.Id == orderId, ct) ?? throw new Marketplace.Domain.Common.DomainException("Order not found.");
    if (order.SellerId != seller.Id) throw new Marketplace.Domain.Common.DomainException("You do not own this order.");
    var shipment = await db.Shipments.SingleOrDefaultAsync(x => x.OrderId == orderId && x.SellerId == seller.Id, ct) ?? throw new Marketplace.Domain.Common.DomainException("Shipment not found.");
    var occurredAt = request.OccurredAtUtc ?? DateTime.UtcNow;
    var latest = await db.ShipmentTrackingEvents.Where(x => x.ShipmentId == shipment.Id).OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id).Select(x => (DateTime?)x.OccurredAtUtc).FirstOrDefaultAsync(ct);
    if (latest.HasValue && occurredAt < latest.Value) throw new Marketplace.Domain.Common.DomainException("Tracking event time cannot precede the previous event.");
    var now = DateTime.UtcNow;
    shipment.ChangeStatus(request.Status, now);
    var trackingEvent = Marketplace.Domain.Shipping.ShipmentTrackingEvent.Create(await ids.NextAsync(ct), shipment.Id, request.Status, request.Description, request.Location, sellerUserId, occurredAt);
    var customerText = request.Status == Marketplace.Domain.Shipping.ShipmentStatus.CarrierDelivered
        ? $"شرکت حمل‌ونقل وضعیت تحویل سفارش شماره {order.Id} را ثبت کرده است. برای تأیید نهایی تحویل و آزادسازی وجه، فرایند تأیید سفارش همچنان لازم است."
        : $"وضعیت ارسال سفارش شماره {order.Id} به «{request.Status}» تغییر کرد.";
    var notification = Marketplace.Domain.Notifications.Notification.Create(await ids.NextAsync(ct), order.CustomerId, Marketplace.Domain.Notifications.NotificationChannel.InApp, "به‌روزرسانی رهگیری سفارش", customerText, "Order", order.Id);
    notification.MarkSent();
    db.ShipmentTrackingEvents.Add(trackingEvent);
    db.Notifications.Add(notification);
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return Results.Ok(new { shipment.Id, shipment.OrderId, status = shipment.Status.ToString(), shipment.TrackingNumber, shipment.UpdatedAtUtc, eventId = trackingEvent.Id });
}).RequirePermission("Order.Delivery.Confirm");

app.MapGet("/api/orders/{orderId:long}/shipment", async (
    System.Security.Claims.ClaimsPrincipal user, long orderId, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    if (!await db.Orders.AsNoTracking().AnyAsync(x => x.Id == orderId && x.CustomerId == customerId, ct))
        throw new Marketplace.Domain.Common.DomainException("Order not found.");
    var shipment = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId, ct);
    if (shipment is null) return Results.NotFound(new { detail = "Shipment has not been registered yet." });
    var events = await db.ShipmentTrackingEvents.AsNoTracking().Where(x => x.ShipmentId == shipment.Id).OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id)
        .Select(x => new { x.Id, status = x.Status.ToString(), x.Description, x.Location, x.OccurredAtUtc }).ToListAsync(ct);
    return Results.Ok(new { shipment.Id, shipment.OrderId, shipment.CarrierName, shipment.TrackingNumber, shipment.TrackingUrl, status = shipment.Status.ToString(), shipment.CreatedAtUtc, shipment.UpdatedAtUtc, shipment.ShippedAtUtc, shipment.CarrierDeliveredAtUtc, events });
}).RequirePermission("Order.ReadOwn");

app.MapGet("/api/seller/orders/{orderId:long}/shipment", async (
    System.Security.Claims.ClaimsPrincipal user, long orderId, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    Marketplace.Application.Abstractions.ISellerManagementRepository sellers, CancellationToken ct) =>
{
    var seller = await sellers.GetSellerByUserIdAsync(CurrentUserId(user), ct) ?? throw new UnauthorizedAccessException();
    if (!await db.Orders.AsNoTracking().AnyAsync(x => x.Id == orderId && x.SellerId == seller.Id, ct))
        throw new Marketplace.Domain.Common.DomainException("Order not found.");
    var shipment = await db.Shipments.AsNoTracking().SingleOrDefaultAsync(x => x.OrderId == orderId && x.SellerId == seller.Id, ct);
    if (shipment is null) return Results.NotFound(new { detail = "Shipment has not been registered yet." });
    var events = await db.ShipmentTrackingEvents.AsNoTracking().Where(x => x.ShipmentId == shipment.Id).OrderBy(x => x.OccurredAtUtc).ThenBy(x => x.Id)
        .Select(x => new { x.Id, status = x.Status.ToString(), x.Description, x.Location, x.OccurredAtUtc }).ToListAsync(ct);
    return Results.Ok(new { shipment.Id, shipment.OrderId, shipment.CarrierName, shipment.TrackingNumber, shipment.TrackingUrl, status = shipment.Status.ToString(), shipment.CreatedAtUtc, shipment.UpdatedAtUtc, shipment.ShippedAtUtc, shipment.CarrierDeliveredAtUtc, events });
}).RequirePermission("Order.ReadOwn");

app.MapPost("/api/orders/{orderId:long}/complaints",async(System.Security.Claims.ClaimsPrincipal user,long orderId,ComplaintRequest request,Marketplace.Application.Orders.OrderActorService service,CancellationToken ct)=>{var id=await service.ComplaintAsync(CurrentUserId(user),orderId,request.Reason,ct);return Results.Ok(new{id});}).RequirePermission("Order.Create");

app.MapPost("/api/admin/financial-integrity/reviews", async (
    FinancialIntegrityReviewRequest request,
    System.Security.Claims.ClaimsPrincipal user,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var allowedKinds = new[] { "PaymentOrderMismatch", "PaymentReview", "RefundProcessing", "SettlementOnHold" };
    if (!allowedKinds.Contains(request.Kind, StringComparer.Ordinal))
        throw new Marketplace.Domain.Common.DomainException("Unsupported financial review type.");
    if (!long.TryParse(request.EntityKey, out var entityId) || entityId <= 0)
        throw new Marketplace.Domain.Common.DomainException("Entity key must be a positive numeric ID.");
    if (string.IsNullOrWhiteSpace(request.Note) || request.Note.Trim().Length > 800)
        throw new Marketplace.Domain.Common.DomainException("A review note of at most 800 characters is required.");

    var exists = request.Kind switch
    {
        "PaymentOrderMismatch" or "PaymentReview" => await db.Payments.AsNoTracking().AnyAsync(x => x.Id == entityId, ct),
        "RefundProcessing" => await db.Refunds.AsNoTracking().AnyAsync(x => x.Id == entityId, ct),
        "SettlementOnHold" => await db.Settlements.AsNoTracking().AnyAsync(x => x.Id == entityId, ct),
        _ => false
    };
    if (!exists) return Results.NotFound(new { detail = "Financial record not found." });

    var detailsJson = System.Text.Json.JsonSerializer.Serialize(new
    {
        kind = request.Kind,
        entityId,
        note = request.Note.Trim(),
        reviewState = "Reviewed"
    }, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    var correlationId = http.TraceIdentifier;
    var audit = Marketplace.Domain.Auditing.AdminAuditEvent.Create(
        CurrentUserId(user), "FinancialIntegrity.Reviewed", request.Kind, entityId.ToString(),
        detailsJson, correlationId.Length <= 100 ? correlationId : correlationId[..100]);
    db.AdminAuditEvents.Add(audit);
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { auditId = audit.Id, kind = request.Kind, entityId, reviewState = "Reviewed", createdAtUtc = audit.CreatedAtUtc });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/financial-integrity/reviews", async (
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
    Results.Ok(await db.AdminAuditEvents.AsNoTracking()
        .Where(x => x.Action == "FinancialIntegrity.Reviewed")
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(200)
        .Select(x => new { auditId = x.Id, actorUserId = x.ActorUserId, kind = x.EntityType,
            entityKey = x.EntityKey, detailsJson = x.DetailsJson, x.CorrelationId, x.CreatedAtUtc })
        .ToListAsync(ct))
).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/financial-integrity/cases", async (
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var events = await db.AdminAuditEvents.AsNoTracking()
        .Where(x => x.Action == "FinancialIntegrity.CaseStatusChanged" || x.Action == "FinancialIntegrity.CaseRechecked")
        .OrderByDescending(x => x.CreatedAtUtc)
        .Take(2000)
        .Select(x => new { x.Id, x.ActorUserId, x.EntityType, x.EntityKey, x.Action, x.DetailsJson, x.CorrelationId, x.CreatedAtUtc })
        .ToListAsync(ct);

    var cases = events
        .GroupBy(x => new { x.EntityType, x.EntityKey })
        .Select(g =>
        {
            var latestActivity = g.First();
            var latestStatus = g.FirstOrDefault(x => x.Action == "FinancialIntegrity.CaseStatusChanged");
            if (latestStatus is null) return null;

            string status;
            string note;
            try
            {
                using var statusDocument = System.Text.Json.JsonDocument.Parse(latestStatus.DetailsJson);
                status = statusDocument.RootElement.TryGetProperty("status", out var statusElement) ? statusElement.GetString() ?? "Open" : "Open";
                using var activityDocument = System.Text.Json.JsonDocument.Parse(latestActivity.DetailsJson);
                note = activityDocument.RootElement.TryGetProperty("note", out var noteElement) ? noteElement.GetString() ?? "" : "";
            }
            catch
            {
                status = "Open";
                note = "جزئیات رویداد قابل خواندن نیست";
            }

            return new
            {
                caseId = latestActivity.EntityType + ":" + latestActivity.EntityKey,
                kind = latestActivity.EntityType,
                entityKey = latestActivity.EntityKey,
                status,
                note,
                actorUserId = latestActivity.ActorUserId,
                auditId = latestActivity.Id,
                latestActivity.CorrelationId,
                updatedAtUtc = latestActivity.CreatedAtUtc,
                historyCount = g.Count()
            };
        })
        .Where(x => x is not null)
        .Select(x => x!)
        .OrderBy(x => x.status == "Resolved" || x.status == "FalsePositive" ? 1 : 0)
        .ThenByDescending(x => x.updatedAtUtc)
        .Take(500)
        .ToList();

    return Results.Ok(new { generatedAtUtc = DateTime.UtcNow, cases, itemsTruncated = events.Count == 2000 });
}).RequirePermission("Admin.Settlement.Process");

app.MapPost("/api/admin/financial-integrity/cases/{kind}/{entityKey}/recheck", async (
    string kind,
    string entityKey,
    System.Security.Claims.ClaimsPrincipal user,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var allowedKinds = new[] { "PaymentOrderMismatch", "PaymentReview", "RefundProcessing", "SettlementOnHold" };
    if (!allowedKinds.Contains(kind, StringComparer.Ordinal))
        throw new Marketplace.Domain.Common.DomainException("Unsupported financial case type.");
    if (!long.TryParse(entityKey, out var entityId) || entityId <= 0)
        throw new Marketplace.Domain.Common.DomainException("Entity key must be a positive numeric ID.");

    bool exists;
    bool findingActive;
    string currentState;
    string resultNote;
    switch (kind)
    {
        case "PaymentOrderMismatch":
        {
            var row = await (from payment in db.Payments.AsNoTracking()
                join order in db.Orders.AsNoTracking() on payment.OrderId equals order.Id
                where payment.Id == entityId
                select new { PaymentStatus = payment.Status, OrderStatus = order.Status }).SingleOrDefaultAsync(ct);
            exists = row is not null;
            if (row is null)
            {
                findingActive = false;
                currentState = "RecordNotFound";
                resultNote = "پرداخت یا سفارش مرتبط پیدا نشد.";
            }
            else
            {
                findingActive =
                    (row.PaymentStatus == Marketplace.Domain.Payments.PaymentStatus.Succeeded
                        && (row.OrderStatus == Marketplace.Domain.Orders.OrderStatus.PendingPayment
                            || row.OrderStatus == Marketplace.Domain.Orders.OrderStatus.Cancelled
                            || row.OrderStatus == Marketplace.Domain.Orders.OrderStatus.Refunded))
                    || (row.PaymentStatus == Marketplace.Domain.Payments.PaymentStatus.Refunded
                        && row.OrderStatus != Marketplace.Domain.Orders.OrderStatus.Refunded);
                currentState = $"Payment={row.PaymentStatus};Order={row.OrderStatus}";
                resultNote = findingActive
                    ? "مغایرت پرداخت و سفارش همچنان برقرار است."
                    : "شرط فعلی مغایرت پرداخت و سفارش مشاهده نشد.";
            }
            break;
        }
        case "PaymentReview":
        {
            var row = await db.Payments.AsNoTracking()
                .Where(x => x.Id == entityId)
                .Select(x => new { x.Status })
                .SingleOrDefaultAsync(ct);
            exists = row is not null;
            findingActive = row?.Status == Marketplace.Domain.Payments.PaymentStatus.ReconciliationRequired;
            currentState = row is null ? "RecordNotFound" : row.Status.ToString();
            resultNote = row is null ? "پرداخت پیدا نشد." : findingActive
                ? "پرداخت همچنان نیازمند تطبیق است."
                : "پرداخت دیگر در وضعیت نیازمند تطبیق نیست؛ وضعیت جاری باید جداگانه بررسی شود.";
            break;
        }
        case "RefundProcessing":
        {
            var row = await db.Refunds.AsNoTracking()
                .Where(x => x.Id == entityId)
                .Select(x => new { x.Status })
                .SingleOrDefaultAsync(ct);
            exists = row is not null;
            findingActive = row?.Status == Marketplace.Domain.Refunds.RefundStatus.Processing;
            currentState = row is null ? "RecordNotFound" : row.Status.ToString();
            resultNote = row is null ? "بازپرداخت پیدا نشد." : findingActive
                ? "بازپرداخت همچنان در حال پردازش است."
                : "بازپرداخت دیگر در وضعیت پردازش نیست؛ نتیجه نهایی را بررسی کنید.";
            break;
        }
        case "SettlementOnHold":
        {
            var row = await db.Settlements.AsNoTracking()
                .Where(x => x.Id == entityId)
                .Select(x => new { x.Status })
                .SingleOrDefaultAsync(ct);
            exists = row is not null;
            findingActive = row?.Status == Marketplace.Domain.Finance.SettlementStatus.OnHold;
            currentState = row is null ? "RecordNotFound" : row.Status.ToString();
            resultNote = row is null ? "تسویه پیدا نشد." : findingActive
                ? "تسویه همچنان متوقف است."
                : "تسویه دیگر در وضعیت توقف نیست؛ نتیجه مالی و مرجع انتقال را بررسی کنید.";
            break;
        }
        default:
            throw new Marketplace.Domain.Common.DomainException("Unsupported financial case type.");
    }

    if (!exists) return Results.NotFound(new { detail = "Financial record not found." });

    var detailsJson = System.Text.Json.JsonSerializer.Serialize(new
    {
        kind,
        entityId,
        findingActive,
        currentState,
        note = resultNote,
        checkType = "CurrentDatabaseStateOnly",
        workflowOnly = true
    }, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    var correlationId = http.TraceIdentifier;
    var audit = Marketplace.Domain.Auditing.AdminAuditEvent.Create(
        CurrentUserId(user), "FinancialIntegrity.CaseRechecked", kind,
        entityId.ToString(System.Globalization.CultureInfo.InvariantCulture), detailsJson,
        correlationId.Length <= 100 ? correlationId : correlationId[..100]);
    db.AdminAuditEvents.Add(audit);
    await db.SaveChangesAsync(ct);

    return Results.Ok(new
    {
        auditId = audit.Id,
        kind,
        entityKey = entityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        findingActive,
        currentState,
        note = resultNote,
        checkedAtUtc = audit.CreatedAtUtc,
        correlationId = audit.CorrelationId,
        checkType = "CurrentDatabaseStateOnly",
        workflowOnly = true
    });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/financial-integrity/cases/{kind}/{entityKey}/history", async (
    string kind,
    string entityKey,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var allowedKinds = new[] { "PaymentOrderMismatch", "PaymentReview", "RefundProcessing", "SettlementOnHold" };
    if (!allowedKinds.Contains(kind, StringComparer.Ordinal))
        throw new Marketplace.Domain.Common.DomainException("Unsupported financial case type.");
    if (!long.TryParse(entityKey, out var entityId) || entityId <= 0)
        throw new Marketplace.Domain.Common.DomainException("Entity key must be a positive numeric ID.");

    var history = await db.AdminAuditEvents.AsNoTracking()
        .Where(x => (x.Action == "FinancialIntegrity.CaseStatusChanged" || x.Action == "FinancialIntegrity.CaseRechecked")
            && x.EntityType == kind && x.EntityKey == entityId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        .OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
        .Take(200)
        .Select(x => new
        {
            auditId = x.Id,
            actorUserId = x.ActorUserId,
            x.EntityType,
            x.EntityKey,
            x.Action,
            x.DetailsJson,
            x.CorrelationId,
            x.CreatedAtUtc
        })
        .ToListAsync(ct);

    var items = history.Select(x =>
    {
        string status = "Unknown";
        string note = "";
        bool? findingActive = null;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(x.DetailsJson);
            if (x.Action == "FinancialIntegrity.CaseRechecked")
            {
                if (document.RootElement.TryGetProperty("findingActive", out var activeElement)
                    && activeElement.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False)
                    findingActive = activeElement.GetBoolean();
                status = findingActive == true ? "FindingStillActive" : "RecheckClear";
            }
            else if (document.RootElement.TryGetProperty("status", out var statusElement))
                status = statusElement.GetString() ?? "Unknown";
            if (document.RootElement.TryGetProperty("note", out var noteElement))
                note = noteElement.GetString() ?? "";
        }
        catch
        {
            note = "جزئیات رویداد قابل خواندن نیست";
        }

        return new
        {
            x.auditId,
            x.actorUserId,
            kind = x.EntityType,
            entityKey = x.EntityKey,
            action = x.Action,
            status,
            findingActive,
            note,
            x.CorrelationId,
            x.CreatedAtUtc
        };
    }).ToList();

    return Results.Ok(new
    {
        kind,
        entityKey = entityId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        generatedAtUtc = DateTime.UtcNow,
        items,
        itemsTruncated = history.Count == 200
    });
}).RequirePermission("Admin.Settlement.Process");

app.MapPost("/api/admin/financial-integrity/cases", async (
    FinancialIntegrityCaseStatusRequest request,
    System.Security.Claims.ClaimsPrincipal user,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var allowedKinds = new[] { "PaymentOrderMismatch", "PaymentReview", "RefundProcessing", "SettlementOnHold" };
    var allowedStatuses = new[] { "Open", "InProgress", "AwaitingEvidence", "Resolved", "FalsePositive" };
    if (!allowedKinds.Contains(request.Kind, StringComparer.Ordinal))
        throw new Marketplace.Domain.Common.DomainException("Unsupported financial case type.");
    if (!allowedStatuses.Contains(request.Status, StringComparer.Ordinal))
        throw new Marketplace.Domain.Common.DomainException("Unsupported financial case status.");
    if (!long.TryParse(request.EntityKey, out var entityId) || entityId <= 0)
        throw new Marketplace.Domain.Common.DomainException("Entity key must be a positive numeric ID.");
    if (string.IsNullOrWhiteSpace(request.Note) || request.Note.Trim().Length > 800)
        throw new Marketplace.Domain.Common.DomainException("A case status note of at most 800 characters is required.");

    var exists = request.Kind switch
    {
        "PaymentOrderMismatch" or "PaymentReview" => await db.Payments.AsNoTracking().AnyAsync(x => x.Id == entityId, ct),
        "RefundProcessing" => await db.Refunds.AsNoTracking().AnyAsync(x => x.Id == entityId, ct),
        "SettlementOnHold" => await db.Settlements.AsNoTracking().AnyAsync(x => x.Id == entityId, ct),
        _ => false
    };
    if (!exists) return Results.NotFound(new { detail = "Financial record not found." });

    var detailsJson = System.Text.Json.JsonSerializer.Serialize(new
    {
        kind = request.Kind,
        entityId,
        status = request.Status,
        note = request.Note.Trim(),
        workflowOnly = true
    });
    var correlationId = http.TraceIdentifier;
    var audit = Marketplace.Domain.Auditing.AdminAuditEvent.Create(
        CurrentUserId(user), "FinancialIntegrity.CaseStatusChanged", request.Kind,
        entityId.ToString(System.Globalization.CultureInfo.InvariantCulture), detailsJson, correlationId);
    db.AdminAuditEvents.Add(audit);
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { auditId = audit.Id, kind = request.Kind, entityKey = entityId.ToString(), status = request.Status, updatedAtUtc = audit.CreatedAtUtc, workflowOnly = true });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/financial-integrity/ledger", async (
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var balances = await db.SellerBalances.AsNoTracking()
        .Select(x => new
        {
            x.SellerId, x.AvailableIRR, x.PendingIRR, x.BlockedIRR,
            x.ReservedForSettlementIRR, x.LiabilityIRR, x.UpdatedAtUtc
        }).ToListAsync(ct);

    var latestLedger = await db.BalanceTransactions.AsNoTracking()
        .GroupBy(x => new { x.SellerId, x.Bucket })
        .Select(g => g.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
            .Select(x => new
            {
                x.SellerId, Bucket = (byte)x.Bucket, x.Id,
                x.BalanceAfterIRR, x.CreatedAtUtc, Type = (byte)x.Type
            }).First())
        .ToListAsync(ct);

    var activeSettlementTotals = await db.Settlements.AsNoTracking()
        .Where(x => x.Status == Marketplace.Domain.Finance.SettlementStatus.Requested
            || x.Status == Marketplace.Domain.Finance.SettlementStatus.Processing
            || x.Status == Marketplace.Domain.Finance.SettlementStatus.OnHold)
        .GroupBy(x => x.SellerId)
        .Select(g => new { SellerId = g.Key, AmountIRR = g.Sum(x => x.AmountIRR), Count = g.Count() })
        .ToListAsync(ct);

    var balanceBySeller = balances.ToDictionary(x => x.SellerId);
    var ledgerByKey = latestLedger.ToDictionary(x => (x.SellerId, x.Bucket));
    var activeBySeller = activeSettlementTotals.ToDictionary(x => x.SellerId);
    var sellerIds = balances.Select(x => x.SellerId)
        .Concat(latestLedger.Select(x => x.SellerId))
        .Concat(activeSettlementTotals.Select(x => x.SellerId))
        .Distinct().ToArray();
    var findings = new List<FinancialLedgerFinding>();

    foreach (var sellerId in sellerIds)
    {
        balanceBySeller.TryGetValue(sellerId, out var balance);
        activeBySeller.TryGetValue(sellerId, out var active);
        if (balance is null)
        {
            findings.Add(new FinancialLedgerFinding(sellerId, "MissingSellerBalance", "All",
                null, null, null, null, null, active?.AmountIRR));
            continue;
        }

        var buckets = new[]
        {
            (Id: (byte)1, Name: "Available", Amount: balance.AvailableIRR),
            (Id: (byte)2, Name: "Pending", Amount: balance.PendingIRR),
            (Id: (byte)3, Name: "Blocked", Amount: balance.BlockedIRR),
            (Id: (byte)4, Name: "ReservedForSettlement", Amount: balance.ReservedForSettlementIRR),
            (Id: (byte)5, Name: "Liability", Amount: balance.LiabilityIRR)
        };

        foreach (var bucket in buckets)
        {
            if (!ledgerByKey.TryGetValue((sellerId, bucket.Id), out var snapshot))
            {
                if (bucket.Amount != 0)
                    findings.Add(new FinancialLedgerFinding(sellerId, "MissingLedgerSnapshot", bucket.Name,
                        bucket.Amount, null, null, null, null, null));
                continue;
            }

            if (snapshot.BalanceAfterIRR != bucket.Amount)
                findings.Add(new FinancialLedgerFinding(sellerId, "BalanceSnapshotMismatch", bucket.Name,
                    bucket.Amount, snapshot.BalanceAfterIRR, bucket.Amount - snapshot.BalanceAfterIRR,
                    snapshot.Id, snapshot.CreatedAtUtc, null));
        }

        var expectedReserved = active?.AmountIRR ?? 0;
        if (balance.ReservedForSettlementIRR != expectedReserved)
            findings.Add(new FinancialLedgerFinding(sellerId, "ReservedSettlementMismatch", "ReservedForSettlement",
                balance.ReservedForSettlementIRR, null,
                balance.ReservedForSettlementIRR - expectedReserved, null, null, expectedReserved));
    }

    var ordered = findings.OrderBy(x => x.SellerId).ThenBy(x => x.FindingType).ThenBy(x => x.Bucket).ToList();
    return Results.Ok(new
    {
        generatedAtUtc = DateTime.UtcNow,
        sellerCount = sellerIds.Length,
        findingCount = ordered.Count,
        countsByType = ordered.GroupBy(x => x.FindingType)
            .ToDictionary(g => g.Key, g => g.Count()),
        items = ordered.Take(200).ToList(),
        itemsTruncated = ordered.Count > 200
    });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/financial-integrity/order-flows", async (
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var commissionIssuesQuery =
        from commission in db.Commissions.AsNoTracking()
        join order in db.Orders.AsNoTracking() on commission.OrderId equals order.Id
        where commission.SellerId != order.SellerId
            || commission.OrderAmountIRR != order.TotalAmountIRR
            || commission.SellerAmountIRR != commission.OrderAmountIRR - commission.CommissionAmountIRR
        select new FinancialOrderFlowFinding(
            "CommissionOrderMismatch", commission.OrderId, commission.Id, null,
            commission.SellerId, order.SellerId, commission.OrderAmountIRR,
            order.TotalAmountIRR, commission.CommissionAmountIRR, commission.SellerAmountIRR,
            commission.CreatedAtUtc);

    var completedRefundsWithoutLedgerQuery =
        from refund in db.Refunds.AsNoTracking()
        join order in db.Orders.AsNoTracking() on refund.OrderId equals order.Id
        where refund.Status == Marketplace.Domain.Refunds.RefundStatus.Completed
            && !db.BalanceTransactions.AsNoTracking().Any(bt =>
                bt.Type == Marketplace.Domain.Finance.BalanceTransactionType.Refund
                && (bt.RefundId == refund.Id
                    || (bt.RefundId == null && bt.OrderId == refund.OrderId && bt.SellerId == order.SellerId)))
        select new FinancialOrderFlowFinding(
            "CompletedRefundMissingLedger", refund.OrderId, refund.Id, refund.Id,
            order.SellerId, order.SellerId, refund.AmountIRR, null, null, null,
            refund.CompletedAtUtc ?? refund.RequestedAtUtc);

    var completedRefundsWithoutCommissionReversalQuery =
        from refund in db.Refunds.AsNoTracking()
        join commission in db.Commissions.AsNoTracking() on refund.OrderId equals commission.OrderId
        where refund.Status == Marketplace.Domain.Refunds.RefundStatus.Completed
            && !db.CommissionReversals.AsNoTracking().Any(reversal => reversal.RefundId == refund.Id)
        select new FinancialOrderFlowFinding(
            "CompletedRefundMissingCommissionReversal", refund.OrderId, commission.Id, refund.Id,
            commission.SellerId, null, refund.AmountIRR, null, commission.CommissionAmountIRR, null,
            refund.CompletedAtUtc ?? refund.RequestedAtUtc);

    var reversalIssuesQuery =
        from reversal in db.CommissionReversals.AsNoTracking()
        join refund in db.Refunds.AsNoTracking() on reversal.RefundId equals refund.Id
        join commission in db.Commissions.AsNoTracking() on reversal.CommissionId equals commission.Id
        where reversal.OrderId != refund.OrderId
            || reversal.OrderId != commission.OrderId
            || reversal.ReversedCommissionIRR > commission.CommissionAmountIRR
            || reversal.RefundAmountIRR != refund.AmountIRR
        select new FinancialOrderFlowFinding(
            "CommissionReversalMismatch", reversal.OrderId, reversal.Id, reversal.RefundId,
            commission.SellerId, null, reversal.RefundAmountIRR, refund.AmountIRR,
            reversal.ReversedCommissionIRR, commission.CommissionAmountIRR,
            reversal.CreatedAtUtc);

    var issues = new List<FinancialOrderFlowFinding>();
    issues.AddRange(await commissionIssuesQuery.OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(ct));
    issues.AddRange(await completedRefundsWithoutLedgerQuery.OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(ct));
    issues.AddRange(await completedRefundsWithoutCommissionReversalQuery.OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(ct));
    issues.AddRange(await reversalIssuesQuery.OrderByDescending(x => x.CreatedAtUtc).Take(200).ToListAsync(ct));

    var commissionIssueCount = await commissionIssuesQuery.CountAsync(ct);
    var missingRefundLedgerCount = await completedRefundsWithoutLedgerQuery.CountAsync(ct);
    var missingCommissionReversalCount = await completedRefundsWithoutCommissionReversalQuery.CountAsync(ct);
    var reversalIssueCount = await reversalIssuesQuery.CountAsync(ct);
    var allCount = commissionIssueCount + missingRefundLedgerCount
        + missingCommissionReversalCount + reversalIssueCount;
    var ordered = issues.OrderByDescending(x => x.CreatedAtUtc)
        .ThenBy(x => x.FindingType).ThenBy(x => x.EntityId).Take(200).ToList();

    return Results.Ok(new
    {
        generatedAtUtc = DateTime.UtcNow,
        findingCount = allCount,
        countsByType = new Dictionary<string, int>
        {
            ["CommissionOrderMismatch"] = commissionIssueCount,
            ["CompletedRefundMissingLedger"] = missingRefundLedgerCount,
            ["CompletedRefundMissingCommissionReversal"] = missingCommissionReversalCount,
            ["CommissionReversalMismatch"] = reversalIssueCount
        },
        items = ordered,
        itemsTruncated = allCount > ordered.Count
    });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/financial-integrity/order-trace/{orderId:long}", async (
    long orderId,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    if (orderId <= 0) return Results.BadRequest(new { title = "Invalid order ID" });

    var order = await db.Orders.AsNoTracking()
        .Where(x => x.Id == orderId)
        .Select(x => new
        {
            x.Id, x.CustomerId, x.SellerId, x.StoreId, x.SubtotalAmountIRR,
            x.CampaignDiscountIRR, x.CouponDiscountIRR, x.TotalAmountIRR,
            x.SellerAmountIRR, Status = (int)x.Status, x.CreatedAtUtc,
            x.PaidAtUtc, x.DeliveredAtUtc
        }).SingleOrDefaultAsync(ct);
    if (order is null) return Results.NotFound(new { title = "Order not found" });

    var payments = await db.Payments.AsNoTracking()
        .Where(x => x.OrderId == orderId)
        .OrderBy(x => x.CreatedAtUtc).Take(100)
        .Select(x => new
        {
            paymentId = x.Id, x.OrderId, x.CustomerId, x.AmountIRR,
            status = (int)x.Status, x.Provider, x.Authority, x.ReferenceNumber,
            x.CreatedAtUtc, x.PaidAtUtc, x.RefundedAtUtc
        }).ToListAsync(ct);
    var paymentIds = payments.Select(x => x.paymentId).ToArray();
    var paymentTransactions = await db.PaymentTransactions.AsNoTracking()
        .Where(x => paymentIds.Contains(x.PaymentId))
        .OrderBy(x => x.CreatedAtUtc).Take(300)
        .Select(x => new
        {
            x.Id, x.PaymentId, x.AmountIRR, status = (int)x.Status,
            x.Provider, x.Authority, x.Reference, x.CreatedAtUtc
        }).ToListAsync(ct);

    var refunds = await db.Refunds.AsNoTracking()
        .Where(x => x.OrderId == orderId)
        .OrderBy(x => x.RequestedAtUtc).Take(100)
        .Select(x => new
        {
            refundId = x.Id, x.OrderId, x.PaymentId, x.CustomerId, x.AmountIRR,
            status = (int)x.Status, reason = (int)x.Reason, x.ProviderReference,
            x.FailureReason, x.RequestedAtUtc, x.CompletedAtUtc
        }).ToListAsync(ct);
    var refundIds = refunds.Select(x => x.refundId).ToArray();
    var reversals = await db.CommissionReversals.AsNoTracking()
        .Where(x => refundIds.Contains(x.RefundId))
        .OrderBy(x => x.CreatedAtUtc).Take(200)
        .Select(x => new
        {
            x.Id, x.CommissionId, x.OrderId, x.RefundId, x.RefundAmountIRR,
            x.ReversedCommissionIRR, x.CreatedAtUtc
        }).ToListAsync(ct);

    var commission = await db.Commissions.AsNoTracking()
        .Where(x => x.OrderId == orderId)
        .Select(x => new
        {
            x.Id, x.OrderId, x.StoreId, x.SellerId, x.OrderAmountIRR,
            x.CommissionRate, x.MinimumCommissionIRR, x.CalculatedCommissionIRR,
            x.CommissionAmountIRR, x.SellerAmountIRR, x.CreatedAtUtc
        }).SingleOrDefaultAsync(ct);

    var settlements = await db.Settlements.AsNoTracking()
        .Where(x => x.SellerId == order.SellerId)
        .OrderByDescending(x => x.RequestedAtUtc).Take(50)
        .Select(x => new
        {
            settlementId = x.Id, x.SellerId, x.AmountIRR, status = (int)x.Status,
            x.BankNameSnapshot, x.Reference, x.FailureReason, x.RequestedAtUtc,
            x.CompletedAtUtc
        }).ToListAsync(ct);
    var settlementIds = settlements.Select(x => x.settlementId).ToArray();

    var ledger = await db.BalanceTransactions.AsNoTracking()
        .Where(x => x.OrderId == orderId
            || (x.RefundId.HasValue && refundIds.Contains(x.RefundId.Value))
            || (x.SettlementId.HasValue && settlementIds.Contains(x.SettlementId.Value)))
        .OrderBy(x => x.CreatedAtUtc).Take(500)
        .Select(x => new
        {
            x.Id, x.SellerId, x.OrderId, x.RefundId, x.SettlementId,
            type = (int)x.Type, bucket = (int)x.Bucket, x.AmountIRR,
            x.BalanceBeforeIRR, x.BalanceAfterIRR, x.Reference, x.CreatedAtUtc
        }).ToListAsync(ct);
    var sellerBalance = await db.SellerBalances.AsNoTracking()
        .Where(x => x.SellerId == order.SellerId)
        .Select(x => new
        {
            x.SellerId, x.AvailableIRR, x.PendingIRR, x.BlockedIRR,
            x.ReservedForSettlementIRR, x.LiabilityIRR, WithdrawableIRR = Math.Max(0, x.AvailableIRR - x.ReservedForSettlementIRR),
            x.UpdatedAtUtc
        }).SingleOrDefaultAsync(ct);

    var findings = new List<object>();
    if (payments.Count == 0)
        findings.Add(new { code = "OrderHasNoPayment", severity = "warning", message = "برای این سفارش رکورد پرداختی پیدا نشد." });
    if (payments.Any(x => x.AmountIRR != order.TotalAmountIRR))
        findings.Add(new { code = "PaymentAmountMismatch", severity = "error", message = "مبلغ حداقل یکی از پرداخت‌ها با مبلغ سفارش متفاوت است." });
    if (payments.Any(x => x.status == (int)Marketplace.Domain.Payments.PaymentStatus.Succeeded)
        && (order.Status == (int)Marketplace.Domain.Orders.OrderStatus.PendingPayment
            || order.Status == (int)Marketplace.Domain.Orders.OrderStatus.Cancelled
            || order.Status == (int)Marketplace.Domain.Orders.OrderStatus.Refunded))
        findings.Add(new { code = "SucceededPaymentOrderMismatch", severity = "error", message = "پرداخت موفق ثبت شده اما وضعیت سفارش با آن سازگار نیست." });
    if (payments.Any(x => x.status == (int)Marketplace.Domain.Payments.PaymentStatus.Refunded)
        && order.Status != (int)Marketplace.Domain.Orders.OrderStatus.Refunded)
        findings.Add(new { code = "RefundedPaymentOrderMismatch", severity = "error", message = "پرداخت بازپرداخت‌شده است اما سفارش در وضعیت بازپرداخت‌شده نیست." });
    if (commission is null)
        findings.Add(new { code = "CommissionMissing", severity = "warning", message = "رکورد کمیسیون برای سفارش وجود ندارد." });
    else
    {
        if (commission.SellerId != order.SellerId || commission.OrderAmountIRR != order.TotalAmountIRR
            || commission.SellerAmountIRR != order.SellerAmountIRR
            || commission.SellerAmountIRR != commission.OrderAmountIRR - commission.CommissionAmountIRR)
            findings.Add(new { code = "CommissionOrderMismatch", severity = "error", message = "مبلغ یا فروشنده ثبت‌شده در کمیسیون با سفارش هم‌خوانی ندارد." });
    }
    if (!ledger.Any(x => x.OrderId == orderId && x.type == (int)Marketplace.Domain.Finance.BalanceTransactionType.Sale))
        findings.Add(new { code = "SaleLedgerMissing", severity = "warning", message = "ثبت فروش مرتبط با سفارش در دفتر مالی پیدا نشد." });

    foreach (var refund in refunds.Where(x => x.status == (int)Marketplace.Domain.Refunds.RefundStatus.Completed))
    {
        var refundLedgerExists = ledger.Any(x => x.type == (int)Marketplace.Domain.Finance.BalanceTransactionType.Refund
            && (x.RefundId == refund.refundId || (x.RefundId is null && x.OrderId == orderId)));
        if (!refundLedgerExists)
            findings.Add(new { code = "RefundLedgerMissing", severity = "error", message = $"بازپرداخت #{refund.refundId} تکمیل شده اما ثبت دفتر متناظر پیدا نشد." });
        if (!reversals.Any(x => x.RefundId == refund.refundId))
            findings.Add(new { code = "CommissionReversalMissing", severity = "warning", message = $"برای بازپرداخت #{refund.refundId} برگشت کمیسیون پیدا نشد." });
    }
    if (sellerBalance is null)
        findings.Add(new { code = "SellerBalanceMissing", severity = "warning", message = "رکورد مانده فعلی فروشنده وجود ندارد." });

    return Results.Ok(new
    {
        generatedAtUtc = DateTime.UtcNow,
        scopeNote = "تسویه‌ها و مانده، در سطح تجمیعی فروشنده‌اند؛ نسبت‌دادن یک تسویه به این سفارش بدون لینک دفتر مالی صریح انجام نشده است.",
        order,
        payments,
        paymentTransactions,
        commission,
        refunds,
        commissionReversals = reversals,
        ledgerTransactions = ledger,
        sellerBalance,
        sellerSettlements = settlements,
        findings,
        findingCount = findings.Count,
        itemsTruncated = payments.Count >= 100 || paymentTransactions.Count >= 300
            || refunds.Count >= 100 || reversals.Count >= 200 || ledger.Count >= 500 || settlements.Count >= 50
    });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/financial-integrity/summary", async (Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var paymentReview = await db.Payments.AsNoTracking()
        .CountAsync(x => x.Status == Marketplace.Domain.Payments.PaymentStatus.ReconciliationRequired, ct);
    var successfulPaymentOrderMismatch = await (
        from payment in db.Payments.AsNoTracking()
        join order in db.Orders.AsNoTracking() on payment.OrderId equals order.Id
        where payment.Status == Marketplace.Domain.Payments.PaymentStatus.Succeeded
            && (order.Status == Marketplace.Domain.Orders.OrderStatus.PendingPayment
                || order.Status == Marketplace.Domain.Orders.OrderStatus.Cancelled
                || order.Status == Marketplace.Domain.Orders.OrderStatus.Refunded)
        select payment.Id).CountAsync(ct);
    var refundedPaymentOrderMismatch = await (
        from payment in db.Payments.AsNoTracking()
        join order in db.Orders.AsNoTracking() on payment.OrderId equals order.Id
        where payment.Status == Marketplace.Domain.Payments.PaymentStatus.Refunded
            && order.Status != Marketplace.Domain.Orders.OrderStatus.Refunded
        select payment.Id).CountAsync(ct);
    var processingRefunds = await db.Refunds.AsNoTracking()
        .CountAsync(x => x.Status == Marketplace.Domain.Refunds.RefundStatus.Processing, ct);
    var settlementsOnHold = await db.Settlements.AsNoTracking()
        .CountAsync(x => x.Status == Marketplace.Domain.Finance.SettlementStatus.OnHold, ct);
    return Results.Ok(new
    {
        generatedAtUtc = DateTime.UtcNow, paymentReview, successfulPaymentOrderMismatch,
        refundedPaymentOrderMismatch, processingRefunds, settlementsOnHold,
        totalReviewItems = paymentReview + successfulPaymentOrderMismatch + refundedPaymentOrderMismatch + processingRefunds + settlementsOnHold
    });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/financial-integrity/items", async (Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var successfulPaymentOrderMismatch = await (
        from payment in db.Payments.AsNoTracking()
        join order in db.Orders.AsNoTracking() on payment.OrderId equals order.Id
        where payment.Status == Marketplace.Domain.Payments.PaymentStatus.Succeeded
            && (order.Status == Marketplace.Domain.Orders.OrderStatus.PendingPayment
                || order.Status == Marketplace.Domain.Orders.OrderStatus.Cancelled
                || order.Status == Marketplace.Domain.Orders.OrderStatus.Refunded)
        orderby payment.CreatedAtUtc descending
        select new { paymentId = payment.Id, orderId = order.Id, customerId = payment.CustomerId,
            amountIRR = payment.AmountIRR, paymentStatus = (byte)payment.Status, orderStatus = (byte)order.Status,
            payment.CreatedAtUtc, provider = payment.Provider, reference = payment.ReferenceNumber })
        .Take(100).ToListAsync(ct);

    var refundedPaymentOrderMismatch = await (
        from payment in db.Payments.AsNoTracking()
        join order in db.Orders.AsNoTracking() on payment.OrderId equals order.Id
        where payment.Status == Marketplace.Domain.Payments.PaymentStatus.Refunded
            && order.Status != Marketplace.Domain.Orders.OrderStatus.Refunded
        orderby payment.CreatedAtUtc descending
        select new { paymentId = payment.Id, orderId = order.Id, customerId = payment.CustomerId,
            amountIRR = payment.AmountIRR, paymentStatus = (byte)payment.Status, orderStatus = (byte)order.Status,
            payment.CreatedAtUtc, provider = payment.Provider, reference = payment.ReferenceNumber })
        .Take(100).ToListAsync(ct);

    var processingRefunds = await (
        from refund in db.Refunds.AsNoTracking()
        where refund.Status == Marketplace.Domain.Refunds.RefundStatus.Processing
        orderby refund.RequestedAtUtc
        select new { refundId = refund.Id, refund.OrderId, refund.PaymentId, refund.CustomerId,
            refund.AmountIRR, status = (byte)refund.Status, refund.RequestedAtUtc, refund.ProviderReference, refund.FailureReason })
        .Take(100).ToListAsync(ct);

    var settlementsOnHold = await (
        from settlement in db.Settlements.AsNoTracking()
        where settlement.Status == Marketplace.Domain.Finance.SettlementStatus.OnHold
        orderby settlement.RequestedAtUtc
        select new { settlementId = settlement.Id, settlement.SellerId, settlement.AmountIRR,
            status = (byte)settlement.Status, settlement.RequestedAtUtc, settlement.Reference, settlement.FailureReason })
        .Take(100).ToListAsync(ct);

    return Results.Ok(new { generatedAtUtc = DateTime.UtcNow, successfulPaymentOrderMismatch,
        refundedPaymentOrderMismatch, processingRefunds, settlementsOnHold });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/payments/reconciliation",async(Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,CancellationToken ct)=>
    Results.Ok(await db.Payments.AsNoTracking()
        .Where(x=>x.Status==Marketplace.Domain.Payments.PaymentStatus.ReconciliationRequired)
        .OrderBy(x=>x.CreatedAtUtc)
        .Take(200)
        .Select(x=>new { paymentId=x.Id,orderId=x.OrderId,customerId=x.CustomerId,amountIRR=x.AmountIRR,provider=x.Provider,reference=x.ReferenceNumber,createdAtUtc=x.CreatedAtUtc })
        .ToListAsync(ct))).RequirePermission("Admin.Settlement.Process");

app.MapPost("/api/admin/payments/{paymentId:long}/reconcile",async(long paymentId,PaymentReconciliationRequest request,System.Security.Claims.ClaimsPrincipal user,Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,CancellationToken ct)=>
{
    if(string.IsNullOrWhiteSpace(request.Note) || request.Note.Trim().Length>2000)
        throw new Marketplace.Domain.Common.DomainException("A note of at most 2000 characters is required.");
    if(request.Action is not ("RefundCompleted" or "KeepOpen"))
        throw new Marketplace.Domain.Common.DomainException("Unsupported reconciliation action.");
    if(request.Action=="RefundCompleted" && string.IsNullOrWhiteSpace(request.BankReference))
        throw new Marketplace.Domain.Common.DomainException("Bank reference is required when confirming a completed refund.");
    if(request.BankReference?.Trim().Length>200)
        throw new Marketplace.Domain.Common.DomainException("Bank reference cannot exceed 200 characters.");

    await using var tx=await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable,ct);
    var payment=await db.Payments.SingleOrDefaultAsync(x=>x.Id==paymentId,ct)
        ??throw new Marketplace.Domain.Common.DomainException("Payment not found.");
    if(payment.Status!=Marketplace.Domain.Payments.PaymentStatus.ReconciliationRequired)
        throw new Marketplace.Domain.Common.DomainException("Payment is not awaiting reconciliation.");
    var adminId=CurrentUserId(user);
    if(request.Action=="RefundCompleted")
        payment.MarkRefunded();

    db.PaymentReconciliationAudits.Add(Marketplace.Domain.Payments.PaymentReconciliationAudit.Create(
        payment.Id,adminId,request.Action,request.Note,request.BankReference));
    await db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
    return Results.Ok(new { paymentId, status=payment.Status.ToString(), action=request.Action });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/payments/reconciliation/history",async(Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,CancellationToken ct)=>
    Results.Ok(await (from a in db.PaymentReconciliationAudits.AsNoTracking()
                      join p in db.Payments.AsNoTracking() on a.PaymentId equals p.Id
                      orderby a.CreatedAtUtc descending
                      select new { auditId=a.Id,paymentId=p.Id,orderId=p.OrderId,customerId=p.CustomerId,amountIRR=p.AmountIRR,action=a.Action,note=a.Note,bankReference=a.BankReference,adminUserId=a.AdminUserId,createdAtUtc=a.CreatedAtUtc })
                     .Take(200).ToListAsync(ct))).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/refunds",async(Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,CancellationToken ct)=>
    Results.Ok(await (from r in db.Refunds.AsNoTracking()
                      join o in db.Orders.AsNoTracking() on r.OrderId equals o.Id
                      orderby r.RequestedAtUtc descending
                      select new { id=r.Id,orderId=r.OrderId,sellerId=o.SellerId,customerId=r.CustomerId,amountIRR=r.AmountIRR,reason=r.Reason.ToString(),status=r.Status.ToString(),r.ProviderReference,r.FailureReason,r.RequestedAtUtc,r.CompletedAtUtc })
                     .Take(200).ToListAsync(ct))).RequirePermission("Admin.Settlement.Process");

app.MapPost("/api/admin/refunds/{refundId:long}/reconcile",async(long refundId,RefundReconciliationRequest request,System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Orders.RefundService service,CancellationToken ct)=>{
    await service.ReconcileAsync(refundId,CurrentUserId(user),request.TransferCompleted,request.BankReference,request.Note,ct);
    return Results.Ok(new { refundId, status=request.TransferCompleted?"Completed":"Failed" });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/refunds/reconciliation/history",async(Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,CancellationToken ct)=>
    Results.Ok(await (from a in db.RefundReconciliationAudits.AsNoTracking()
                      join r in db.Refunds.AsNoTracking() on a.RefundId equals r.Id
                      join o in db.Orders.AsNoTracking() on r.OrderId equals o.Id
                      orderby a.CreatedAtUtc descending
                      select new { auditId=a.Id,refundId=r.Id,orderId=r.OrderId,customerId=r.CustomerId,sellerId=o.SellerId,amountIRR=r.AmountIRR,transferCompleted=a.TransferCompleted,note=a.Note,bankReference=a.BankReference,adminUserId=a.AdminUserId,createdAtUtc=a.CreatedAtUtc })
                     .Take(200).ToListAsync(ct))).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/me/complaints", async (System.Security.Claims.ClaimsPrincipal user, int? take, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    var limit = Math.Clamp(take ?? 100, 1, 200);
    var items = await (
        from complaint in db.Complaints.AsNoTracking()
        join order in db.Orders.AsNoTracking() on complaint.OrderId equals order.Id
        where complaint.CustomerId == customerId
        orderby complaint.CreatedAtUtc descending
        select new
        {
            id = complaint.Id,
            orderId = complaint.OrderId,
            status = (byte)complaint.Status,
            reason = complaint.Reason,
            createdAtUtc = complaint.CreatedAtUtc,
            resolvedAtUtc = complaint.ResolvedAtUtc,
            resolutionNote = complaint.ResolutionNote,
            orderTotalIRR = order.TotalAmountIRR
        }).Take(limit).ToListAsync(ct);
    return Results.Ok(items);
}).RequirePermission("Order.ReadOwn");

app.MapPost("/api/me/complaints/{complaintId:long}/cancel", async (System.Security.Claims.ClaimsPrincipal user, long complaintId, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    var complaint = await db.Complaints.SingleOrDefaultAsync(x => x.Id == complaintId && x.CustomerId == customerId, ct);
    if (complaint is null) return Results.NotFound();
    complaint.Cancel();
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequirePermission("Order.Create");

app.MapGet("/api/admin/complaints",async(Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,CancellationToken ct)=>
    Results.Ok(await (from c in db.Complaints
                      join o in db.Orders on c.OrderId equals o.Id
                      orderby c.CreatedAtUtc descending
                      select new { c.Id,c.OrderId,c.CustomerId,c.SellerId,Status=(byte)c.Status,c.Reason,c.ResolutionNote,c.CreatedAtUtc,c.ResolvedAtUtc,OrderStatus=(byte)o.Status,TotalIRR=o.TotalAmountIRR })
                     .Take(200).ToListAsync(ct))).RequirePermission("Complaint.Resolve");

app.MapPost("/api/complaints/{complaintId:long}/resolve",async(long complaintId,ComplaintResolveRequest request,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    await service.ResolveComplaintAsync(complaintId,request.CustomerWon,request.Note,ct);return Results.Ok();
}).RequirePermission("Complaint.Resolve");

app.MapPost("/api/orders/{orderId:long}/refund", async (
    System.Security.Claims.ClaimsPrincipal user,
    long orderId,
    RefundRequest request,
    Marketplace.Application.Orders.OrderActorService service,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    ILogger<Program> logger,
    CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    // Verify ownership before the recovery lookup so an exception cannot reveal another
    // customer's refund state.
    var ownsOrder = await db.Orders.AsNoTracking()
        .AnyAsync(x => x.Id == orderId && x.CustomerId == customerId, ct);
    if (!ownsOrder) return Results.NotFound();

    try
    {
        await service.RefundAsync(customerId, orderId, request.Reason, ct);
        var completed = await db.Refunds.AsNoTracking()
            .Where(x => x.OrderId == orderId)
            .OrderByDescending(x => x.RequestedAtUtc)
            .Select(x => new { refundId = x.Id, status = x.Status.ToString(), x.ProviderReference })
            .FirstOrDefaultAsync(ct);

        return Results.Ok(completed is null
            ? new { refundId = (long?)null, status = "Unknown", providerReference = (string?)null, outcomeRequiresReconciliation = false, message = (string?)null }
            : new { refundId = (long?)completed.refundId, status = completed.status, providerReference = completed.ProviderReference, outcomeRequiresReconciliation = false, message = (string?)null });
    }
    catch (Exception exception)
    {
        try
        {
            var persisted = await db.Refunds.AsNoTracking()
                .Where(x => x.OrderId == orderId)
                .OrderByDescending(x => x.RequestedAtUtc)
                .Select(x => new { refundId = x.Id, status = x.Status, x.ProviderReference, x.FailureReason })
                .FirstOrDefaultAsync(CancellationToken.None);

            if (persisted?.status is Marketplace.Domain.Refunds.RefundStatus.Processing
                or Marketplace.Domain.Refunds.RefundStatus.Completed)
            {
                var uncertain = persisted.status == Marketplace.Domain.Refunds.RefundStatus.Processing;
                logger.LogWarning(exception,
                    "Refund {RefundId} for order {OrderId} ended with {RefundStatus}; reconciliationRequired={ReconciliationRequired}.",
                    persisted.refundId, orderId, persisted.status, uncertain);

                var response = new
                {
                    refundId = (long?)persisted.refundId,
                    status = persisted.status.ToString(),
                    providerReference = persisted.ProviderReference,
                    outcomeRequiresReconciliation = uncertain,
                    message = uncertain
                        ? "Refund outcome is not confirmed. Do not submit another refund; an administrator must reconcile the bank result."
                        : "Refund is already completed."
                };
                if (uncertain)
                    return Results.Json(response, statusCode: StatusCodes.Status202Accepted);

                return Results.Ok(response);
            }
        }
        catch (Exception lookupException)
        {
            logger.LogError(lookupException, "Could not read refund state for order {OrderId} after processing failed.", orderId);
        }

        logger.LogError(exception, "Refund processing failed for order {OrderId} without a persisted uncertain or completed result.", orderId);
        throw;
    }
}).RequirePermission("Order.Create");


app.MapGet("/api/sellers/me/campaigns",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
    var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();
    return Results.Ok(await (from c in db.Campaigns
        join s in db.Stores on c.StoreId equals s.Id
        where s.SellerId==seller.Id
        orderby c.StartsAtUtc descending
        select new { c.Id,c.StoreId,StoreName=s.Name,c.Name,DiscountType=(byte)c.DiscountType,c.DiscountValue,c.StartsAtUtc,c.EndsAtUtc,c.IsActive,ProductCount=db.CampaignProducts.Count(t=>t.CampaignId==c.Id) })
        .Take(200).ToListAsync(ct));
}).RequirePermission("Seller.Campaign.Manage");

app.MapGet("/api/sellers/me/coupons",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
    var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();
    return Results.Ok(await (from c in db.Coupons
        join s in db.Stores on c.StoreId equals s.Id
        where c.SellerId==seller.Id
        orderby c.Id descending
        select new { c.Id,c.StoreId,StoreName=s.Name,c.Code,DiscountType=(byte)c.DiscountType,c.DiscountValue,c.MaxDiscountAmountIRR,c.MinimumPurchaseIRR,c.MaxUses,c.NewCustomerOnly,c.StartsAtUtc,c.EndsAtUtc,c.IsActive,ProductCount=db.CouponProducts.Count(t=>t.CouponId==c.Id),CategoryCount=db.CouponCategories.Count(t=>t.CouponId==c.Id),UsedCount=db.CouponUsages.Count(t=>t.CouponId==c.Id) })
        .Take(200).ToListAsync(ct));
}).RequirePermission("Seller.Coupon.Manage");

app.MapPost("/api/sellers/me/campaigns/{campaignId:long}/deactivate",async(System.Security.Claims.ClaimsPrincipal user,long campaignId,Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
    var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();
    var campaign=await db.Campaigns.SingleOrDefaultAsync(x=>x.Id==campaignId,ct)??throw new Marketplace.Domain.Common.DomainException("Campaign not found.");
    if(!await sellers.StoreBelongsToSellerAsync(campaign.StoreId,seller.Id,ct))throw new UnauthorizedAccessException();
    campaign.Deactivate();
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequirePermission("Seller.Campaign.Manage");

app.MapPost("/api/sellers/me/coupons/{couponId:long}/deactivate",async(System.Security.Claims.ClaimsPrincipal user,long couponId,Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
    var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();
    var coupon=await db.Coupons.SingleOrDefaultAsync(x=>x.Id==couponId,ct)??throw new Marketplace.Domain.Common.DomainException("Coupon not found.");
    if(coupon.SellerId!=seller.Id)throw new UnauthorizedAccessException();
    coupon.Deactivate();
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequirePermission("Seller.Coupon.Manage");

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

app.MapGet("/api/sellers/me/inventory/overview",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,string? filter,int? take,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();
 var inventoryQuery=from store in db.Stores
  join product in db.Products on store.Id equals product.StoreId
  join variant in db.ProductVariants on product.Id equals variant.ProductId
  join inventory in db.InventoryItems on variant.Id equals inventory.ProductVariantId
  where store.SellerId==seller.Id
  select new { storeId=store.Id,storeName=store.Name,productId=product.Id,productName=product.Name,variantId=variant.Id,sku=variant.SKU,variantKey=variant.VariantKey,stockQuantity=inventory.StockQuantity,reservedQuantity=inventory.ReservedQuantity,availableQuantity=inventory.StockQuantity-inventory.ReservedQuantity,lowStockThreshold=inventory.LowStockThreshold,isLowStock=inventory.StockQuantity>inventory.ReservedQuantity&&inventory.StockQuantity-inventory.ReservedQuantity<=inventory.LowStockThreshold };
 var totalVariants=await inventoryQuery.CountAsync(ct);
 var lowStockCount=await inventoryQuery.CountAsync(x=>x.isLowStock,ct);
 var outOfStockCount=await inventoryQuery.CountAsync(x=>x.availableQuantity==0,ct);
 var selected=(filter??"all").Trim().ToLowerInvariant();
 if(selected=="low") inventoryQuery=inventoryQuery.Where(x=>x.isLowStock);
 else if(selected=="out") inventoryQuery=inventoryQuery.Where(x=>x.availableQuantity==0);
 else selected="all";
 var items=await inventoryQuery.OrderBy(x=>x.availableQuantity).ThenBy(x=>x.productName).ThenBy(x=>x.sku).Take(Math.Clamp(take??100,1,200)).ToListAsync(ct);
 return Results.Ok(new { totalVariants,lowStockCount,outOfStockCount,filter=selected,items });
}).RequirePermission("Seller.Catalog.Manage");

app.MapGet("/api/sellers/me/variants/{variantId:long}/stock",async(System.Security.Claims.ClaimsPrincipal user,long variantId,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();var stock=await service.GetStockAsync(seller.Id,variantId,ct);return Results.Ok(new { stockQuantity=stock.StockQuantity,reservedQuantity=stock.ReservedQuantity,availableQuantity=stock.AvailableQuantity,lowStockThreshold=stock.LowStockThreshold,isLowStock=stock.IsLowStock });}).RequirePermission("Seller.Catalog.Manage");
app.MapPut("/api/sellers/me/variants/{variantId:long}/stock",async(System.Security.Claims.ClaimsPrincipal user,long variantId,StockRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,Marketplace.Infrastructure.Notifications.LowStockSmsService lowStockSms,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException(); await service.SetStockAsync(seller.Id,variantId,request.Quantity,request.Reason,ct); await lowStockSms.NotifyIfLowAsync(variantId,ct); return Results.NoContent();
}).RequirePermission("Seller.Catalog.Manage");
app.MapPut("/api/sellers/me/variants/{variantId:long}/stock/threshold",async(System.Security.Claims.ClaimsPrincipal user,long variantId,StockThresholdRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,Marketplace.Infrastructure.Notifications.LowStockSmsService lowStockSms,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();await service.SetLowStockThresholdAsync(seller.Id,variantId,request.Threshold,ct);await lowStockSms.NotifyIfLowAsync(variantId,ct);return Results.NoContent();}).RequirePermission("Seller.Catalog.Manage");
app.MapGet("/api/sellers/me/variants/{variantId:long}/stock/movements",async(System.Security.Claims.ClaimsPrincipal user,long variantId,int? take,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();var items=await service.GetStockMovementsAsync(seller.Id,variantId,take??50,ct);return Results.Ok(items.Select(x=>new { x.Id,x.ProductVariantId,x.PreviousStockQuantity,x.NewStockQuantity,x.QuantityDelta,x.Reason,x.CreatedAtUtc }));}).RequirePermission("Seller.Catalog.Manage");

app.MapPost("/api/sellers/me/stores/{storeId:long}/warranties",async(System.Security.Claims.ClaimsPrincipal user,long storeId,WarrantyRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException(); var id=await service.CreateWarrantyAsync(seller.Id,storeId,request.Name,request.PriceIRR,ct); return Results.Ok(new{id});
}).RequirePermission("Seller.Catalog.Manage");

app.MapPost("/api/sellers/me/products/{productId:long}/warranties/{warrantyId:long}",async(System.Security.Claims.ClaimsPrincipal user,long productId,long warrantyId,LinkWarrantyRequest request,Marketplace.Application.Catalog.CatalogManagementService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{
 var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException(); await service.LinkWarrantyAsync(seller.Id,productId,warrantyId,request.IsDefault,ct); return Results.NoContent();
}).RequirePermission("Seller.Catalog.Manage");


app.MapGet("/api/admin/orders", async (
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    int? status,
    long? orderId,
    long? sellerId,
    long? storeId,
    DateTime? fromUtc,
    DateTime? toUtc,
    int page = 1,
    int pageSize = 25,
    CancellationToken ct = default) =>
{
    if (status.HasValue && (status.Value < 1 || status.Value > 10))
        return Results.BadRequest(new { detail = "status must be between 1 and 10." });
    if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
        return Results.BadRequest(new { detail = "fromUtc must not be later than toUtc." });

    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize <= 0 ? 25 : pageSize, 1, 100);
    var statusCountsQuery = db.Orders.AsNoTracking().AsQueryable();
    if (orderId.HasValue) statusCountsQuery = statusCountsQuery.Where(x => x.Id == orderId.Value);
    if (sellerId.HasValue) statusCountsQuery = statusCountsQuery.Where(x => x.SellerId == sellerId.Value);
    if (storeId.HasValue) statusCountsQuery = statusCountsQuery.Where(x => x.StoreId == storeId.Value);
    if (fromUtc.HasValue) statusCountsQuery = statusCountsQuery.Where(x => x.CreatedAtUtc >= fromUtc.Value);
    if (toUtc.HasValue) statusCountsQuery = statusCountsQuery.Where(x => x.CreatedAtUtc <= toUtc.Value);
    var statusCounts = await statusCountsQuery.GroupBy(x => x.Status)
        .Select(g => new { status = (int)g.Key, count = g.LongCount() })
        .ToListAsync(ct);

    var query = statusCountsQuery;
    if (status.HasValue) query = query.Where(x => (int)x.Status == status.Value);
    var total = await query.LongCountAsync(ct);
    var items = await query.OrderByDescending(x => x.CreatedAtUtc).ThenByDescending(x => x.Id)
        .Skip((page - 1) * pageSize).Take(pageSize)
        .Select(x => new
        {
            id = x.Id, customerId = x.CustomerId, sellerId = x.SellerId, storeId = x.StoreId,
            status = (int)x.Status, subtotalIRR = x.SubtotalAmountIRR,
            campaignDiscountIRR = x.CampaignDiscountIRR, couponDiscountIRR = x.CouponDiscountIRR,
            totalIRR = x.TotalAmountIRR, sellerAmountIRR = x.SellerAmountIRR,
            couponCode = x.CouponCodeSnapshot, destinationCity = x.DestinationCityNameSnapshot,
            destinationProvince = x.DestinationProvinceNameSnapshot, createdAtUtc = x.CreatedAtUtc,
            paidAtUtc = x.PaidAtUtc, deliveredAtUtc = x.DeliveredAtUtc
        }).ToListAsync(ct);

    return Results.Ok(new
    {
        page, pageSize, total, pageCount = (int)Math.Ceiling(total / (double)pageSize),
        statusCounts, items
    });
}).RequirePermission("Admin.Order.Read");

app.MapGet("/api/admin/orders/{orderId:long}", async (
    long orderId,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var order = await db.Orders.AsNoTracking().Where(x => x.Id == orderId)
        .Select(x => new
        {
            id = x.Id, customerId = x.CustomerId, sellerId = x.SellerId, storeId = x.StoreId,
            status = (int)x.Status, subtotalIRR = x.SubtotalAmountIRR,
            campaignDiscountIRR = x.CampaignDiscountIRR, couponDiscountIRR = x.CouponDiscountIRR,
            totalIRR = x.TotalAmountIRR, sellerAmountIRR = x.SellerAmountIRR,
            couponCode = x.CouponCodeSnapshot, destinationCity = x.DestinationCityNameSnapshot,
            destinationProvince = x.DestinationProvinceNameSnapshot, createdAtUtc = x.CreatedAtUtc,
            paidAtUtc = x.PaidAtUtc, deliveredAtUtc = x.DeliveredAtUtc,
            deliveryExpiresAtUtc = x.DeliveryExpiresAtUtc, complaintExpiresAtUtc = x.ComplaintExpiresAtUtc
        }).SingleOrDefaultAsync(ct);
    if (order is null) return Results.NotFound();

    var items = await db.OrderItems.AsNoTracking().Where(x => x.OrderId == orderId)
        .OrderBy(x => x.Id)
        .Select(x => new
        {
            id = x.Id, productId = x.ProductId, variantId = x.VariantId,
            productName = x.ProductNameSnapshot, variant = x.VariantSnapshot, warranty = x.WarrantySnapshot,
            quantity = x.Quantity, baseUnitPriceIRR = x.BaseUnitPriceIRR, unitPriceIRR = x.UnitPriceIRR,
            warrantyPriceIRR = x.WarrantyPriceIRR, campaignDiscountIRR = x.CampaignDiscountIRR,
            couponDiscountIRR = x.CouponDiscountIRR, campaignId = x.CampaignId,
            campaignName = x.CampaignNameSnapshot, lineTotalIRR = x.LineTotalIRR
        }).ToListAsync(ct);
    var payment = await db.Payments.AsNoTracking().Where(x => x.OrderId == orderId)
        .OrderByDescending(x => x.CreatedAtUtc)
        .Select(x => new { status = x.Status.ToString(), provider = x.Provider, referenceNumber = x.ReferenceNumber,
            amountIRR = x.AmountIRR, createdAtUtc = x.CreatedAtUtc, paidAtUtc = x.PaidAtUtc })
        .FirstOrDefaultAsync(ct);
    return Results.Ok(new { order, items, payment });
}).RequirePermission("Admin.Order.Read");

app.MapGet("/api/me/saved-products", async (System.Security.Claims.ClaimsPrincipal user, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    var items = await (from saved in db.SavedProducts.AsNoTracking()
        join product in db.Products.AsNoTracking() on saved.ProductId equals product.Id
        join store in db.Stores.AsNoTracking() on product.StoreId equals store.Id
        where saved.CustomerId == customerId && product.Status == Marketplace.Domain.Catalog.ProductStatus.Active
        orderby saved.CreatedAtUtc descending
        select new { saved.Id, productId = product.Id, productName = product.Name, productSlug = product.Slug, product.Description,
            product.BasePriceIRR, product.HasVariants, storeId = store.Id, storeName = store.Name, storeSlug = store.Slug, saved.CreatedAtUtc })
        .Take(200).ToListAsync(ct);
    return Results.Ok(items);
}).RequirePermission("Order.ReadOwn");

app.MapPost("/api/me/saved-products/{productId:long}", async (System.Security.Claims.ClaimsPrincipal user, long productId,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, Marketplace.Application.Abstractions.IIdGenerator ids, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    var product = await db.Products.AsNoTracking()
        .Where(x => x.Id == productId && x.Status == Marketplace.Domain.Catalog.ProductStatus.Active)
        .Select(x => new { x.Id }).SingleOrDefaultAsync(ct);
    if (product is null) return Results.NotFound(new { detail = "محصول فعال پیدا نشد." });
    if (await db.SavedProducts.AnyAsync(x => x.CustomerId == customerId && x.ProductId == productId, ct))
        return Results.Ok(new { productId, saved = true });
    db.SavedProducts.Add(Marketplace.Domain.Catalog.SavedProduct.Create(await ids.NextAsync(ct), customerId, productId));
    try { await db.SaveChangesAsync(ct); }
    catch (Microsoft.EntityFrameworkCore.DbUpdateException)
    {
        db.ChangeTracker.Clear();
        if (!await db.SavedProducts.AsNoTracking().AnyAsync(x => x.CustomerId == customerId && x.ProductId == productId, ct)) throw;
    }
    return Results.Ok(new { productId, saved = true });
}).RequirePermission("Order.ReadOwn");

app.MapDelete("/api/me/saved-products/{productId:long}", async (System.Security.Claims.ClaimsPrincipal user, long productId,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    var saved = await db.SavedProducts.SingleOrDefaultAsync(x => x.CustomerId == customerId && x.ProductId == productId, ct);
    if (saved is not null) { db.SavedProducts.Remove(saved); await db.SaveChangesAsync(ct); }
    return Results.NoContent();
}).RequirePermission("Order.ReadOwn");

app.MapGet("/api/orders",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Orders.OrderQueryService service,CancellationToken ct)=>Results.Ok(await service.GetCustomerOrdersAsync(CurrentUserId(user),ct))).RequirePermission("Order.ReadOwn");
app.MapGet("/api/orders/{orderId:long}",async(System.Security.Claims.ClaimsPrincipal user,long orderId,Marketplace.Application.Orders.OrderQueryService service,CancellationToken ct)=>Results.Ok(await service.GetCustomerOrderAsync(CurrentUserId(user),orderId,ct))).RequirePermission("Order.ReadOwn");
app.MapGet("/api/seller/orders",async(System.Security.Claims.ClaimsPrincipal user,Marketplace.Application.Orders.OrderQueryService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();return Results.Ok(await service.GetSellerOrdersAsync(seller.Id,ct));}).RequirePermission("Order.ReadOwn");
app.MapGet("/api/seller/orders/{orderId:long}",async(System.Security.Claims.ClaimsPrincipal user,long orderId,Marketplace.Application.Orders.OrderQueryService service,Marketplace.Application.Abstractions.ISellerManagementRepository sellers,CancellationToken ct)=>{var seller=await sellers.GetSellerByUserIdAsync(CurrentUserId(user),ct)??throw new UnauthorizedAccessException();return Results.Ok(await service.GetSellerOrderAsync(seller.Id,orderId,ct));}).RequirePermission("Order.ReadOwn");

app.MapGet("/api/admin/outbox/summary", async (
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var statusCounts = await db.OutboxMessages.AsNoTracking()
        .GroupBy(x => x.Status)
        .Select(g => new { status = g.Key, count = g.Count() })
        .ToListAsync(ct);
    var eventCounts = await db.OutboxMessages.AsNoTracking()
        .Where(x => x.Status != "Processed")
        .GroupBy(x => x.EventType)
        .Select(g => new { eventType = g.Key, count = g.Count() })
        .OrderByDescending(x => x.count)
        .Take(20)
        .ToListAsync(ct);
    var recentFailures = await db.OutboxMessages.AsNoTracking()
        .Where(x => x.Status == "DeadLetter" || x.LastError != null)
        .OrderByDescending(x => x.OccurredAtUtc)
        .Take(10)
        .Select(x => new
        {
            x.Id, x.MessageId, x.EventType, x.Status, x.Attempts,
            x.LastError, x.OccurredAtUtc, x.NextAttemptAtUtc, x.ProcessedAtUtc
        }).ToListAsync(ct);

    var counts = statusCounts.ToDictionary(x => x.status, x => x.count);
    return Results.Ok(new
    {
        generatedAtUtc = DateTime.UtcNow,
        total = counts.Values.Sum(),
        pending = counts.GetValueOrDefault("Pending"),
        processing = counts.GetValueOrDefault("Processing"),
        processed = counts.GetValueOrDefault("Processed"),
        deadLetter = counts.GetValueOrDefault("DeadLetter"),
        statusCounts = counts,
        eventCounts,
        recentFailures
    });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/outbox/health", async (
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    Microsoft.Extensions.Configuration.IConfiguration configuration,
    CancellationToken ct) =>
{
    static int ReadThreshold(Microsoft.Extensions.Configuration.IConfiguration config, string key, int fallback, int min, int max)
        => int.TryParse(config[key], out var value) ? Math.Clamp(value, min, max) : fallback;

    var now = DateTime.UtcNow;
    var dispatcherEnabled = bool.TryParse(configuration["Outbox:Enabled"], out var enabled) && enabled;
    var webhookUrlConfigured = Uri.TryCreate(configuration["Outbox:Webhook:Url"], UriKind.Absolute, out var webhookUri)
        && (webhookUri.Scheme == Uri.UriSchemeHttps || (webhookUri.IsLoopback && webhookUri.Scheme == Uri.UriSchemeHttp));
    var webhookSecretConfigured = System.Text.Encoding.UTF8.GetByteCount(configuration["Outbox:Webhook:Secret"] ?? string.Empty) >= 32;
    var publisherConfigured = webhookUrlConfigured && webhookSecretConfigured;
    var pendingAgeMinutes = ReadThreshold(configuration, "Outbox:Health:PendingAgeMinutes", 15, 1, 1440);
    var leaseGraceMinutes = ReadThreshold(configuration, "Outbox:Health:LeaseGraceMinutes", 2, 0, 120);
    var deadLetterWarningCount = ReadThreshold(configuration, "Outbox:Health:DeadLetterWarningCount", 1, 1, 100000);
    var pendingCutoff = now.AddMinutes(-pendingAgeMinutes);
    var staleLeaseCutoff = now.AddMinutes(-leaseGraceMinutes);

    var pendingCount = await db.OutboxMessages.AsNoTracking()
        .CountAsync(x => x.Status == "Pending" && x.OccurredAtUtc <= pendingCutoff && x.NextAttemptAtUtc <= now, ct);
    var staleProcessingCount = await db.OutboxMessages.AsNoTracking()
        .CountAsync(x => x.Status == "Processing" && x.LockedUntilUtc != null && x.LockedUntilUtc <= staleLeaseCutoff, ct);
    var deadLetterCount = await db.OutboxMessages.AsNoTracking()
        .CountAsync(x => x.Status == "DeadLetter", ct);
    var duePendingCount = await db.OutboxMessages.AsNoTracking()
        .CountAsync(x => x.Status == "Pending" && x.NextAttemptAtUtc <= now, ct);

    var oldestPending = await db.OutboxMessages.AsNoTracking()
        .Where(x => x.Status == "Pending" && x.NextAttemptAtUtc <= now)
        .OrderBy(x => x.OccurredAtUtc)
        .Select(x => new { x.Id, x.EventType, x.OccurredAtUtc })
        .FirstOrDefaultAsync(ct);

    var staleExamples = await db.OutboxMessages.AsNoTracking()
        .Where(x => x.Status == "Processing" && x.LockedUntilUtc != null && x.LockedUntilUtc <= staleLeaseCutoff)
        .OrderBy(x => x.LockedUntilUtc)
        .Take(10)
        .Select(x => new { x.Id, x.EventType, x.Attempts, x.LockedUntilUtc, x.LastError })
        .ToListAsync(ct);

    var alerts = new List<object>();
    if (!dispatcherEnabled)
        alerts.Add(new { code = "Outbox.DispatcherDisabled", severity = "Warning", count = 1, message = "Outbox dispatch is disabled; messages are persisted but not sent to the configured receiver." });
    else if (!publisherConfigured)
        alerts.Add(new { code = "Outbox.PublisherNotConfigured", severity = "Critical", count = 1, message = "Outbox dispatch is enabled but the HTTPS webhook URL or signing secret is not configured correctly." });
    if (deadLetterCount >= deadLetterWarningCount)
        alerts.Add(new { code = "Outbox.DeadLetter", severity = "Critical", count = deadLetterCount, message = "One or more messages require manual investigation." });
    if (staleProcessingCount > 0)
        alerts.Add(new { code = "Outbox.StaleProcessing", severity = "Critical", count = staleProcessingCount, message = "Processing leases have expired beyond the configured grace period." });
    if (pendingCount > 0)
        alerts.Add(new { code = "Outbox.PendingBacklog", severity = "Warning", count = pendingCount, message = "Due pending messages have exceeded the configured age threshold." });

    var severity = alerts.Any(x => x.GetType().GetProperty("severity")?.GetValue(x)?.ToString() == "Critical")
        ? "Critical"
        : alerts.Count > 0 ? "Warning" : "Healthy";

    return Results.Ok(new
    {
        generatedAtUtc = now,
        status = severity,
        dispatcher = new { enabled = dispatcherEnabled, webhookConfigured = publisherConfigured },
        retention = new
        {
            enabled = configuration.GetValue<bool>("Outbox:Retention:Enabled"),
            processedRetentionDays = ReadThreshold(configuration, "Outbox:Retention:ProcessedRetentionDays", 90, 7, 3650),
            batchSize = ReadThreshold(configuration, "Outbox:Retention:BatchSize", 500, 1, 5000),
            intervalMinutes = ReadThreshold(configuration, "Outbox:Retention:IntervalMinutes", 60, 5, 1440),
            archiveTableRequired = true
        },
        thresholds = new { pendingAgeMinutes, leaseGraceMinutes, deadLetterWarningCount },
        metrics = new { duePendingCount, overduePendingCount = pendingCount, staleProcessingCount, deadLetterCount },
        oldestDuePending = oldestPending,
        staleProcessingExamples = staleExamples,
        alerts
    });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/outbox/messages", async (
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    string? status,
    string? eventType,
    DateTime? fromUtc,
    DateTime? toUtc,
    CancellationToken ct,
    int page = 1,
    int pageSize = 25) =>
{
    var allowedStatuses = new[] { "Pending", "Processing", "Processed", "DeadLetter" };
    if (!string.IsNullOrWhiteSpace(status) && !allowedStatuses.Contains(status, StringComparer.Ordinal))
        throw new Marketplace.Domain.Common.DomainException("Unsupported outbox status.");
    if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
        throw new Marketplace.Domain.Common.DomainException("fromUtc must not be later than toUtc.");

    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize == 0 ? 25 : pageSize, 1, 100);
    var query = db.OutboxMessages.AsNoTracking().AsQueryable();
    if (!string.IsNullOrWhiteSpace(status)) query = query.Where(x => x.Status == status);
    if (!string.IsNullOrWhiteSpace(eventType))
    {
        var eventTypeFilter = eventType.Trim();
        query = query.Where(x => x.EventType.Contains(eventTypeFilter));
    }
    if (fromUtc.HasValue) query = query.Where(x => x.OccurredAtUtc >= fromUtc.Value);
    if (toUtc.HasValue) query = query.Where(x => x.OccurredAtUtc <= toUtc.Value);

    var total = await query.CountAsync(ct);
    var items = await query.OrderByDescending(x => x.OccurredAtUtc).ThenByDescending(x => x.Id)
        .Skip((page - 1) * pageSize).Take(pageSize)
        .Select(x => new
        {
            x.Id, x.MessageId, x.EventType, x.Status, x.Attempts,
            x.OccurredAtUtc, x.ProcessedAtUtc, x.LockedUntilUtc, x.NextAttemptAtUtc,
            x.LastError,
            payloadPreview = x.PayloadJson.Length > 500 ? x.PayloadJson.Substring(0, 500) : x.PayloadJson
        }).ToListAsync(ct);

    return Results.Ok(new { page, pageSize, total, pageCount = (int)Math.Ceiling(total / (double)pageSize), items });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/outbox/messages/{id:long}", async (
    long id,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var item = await db.OutboxMessages.AsNoTracking().Where(x => x.Id == id)
        .Select(x => new
        {
            x.Id, x.MessageId, x.EventType, x.Status, x.Attempts,
            x.OccurredAtUtc, x.ProcessedAtUtc, x.LockedUntilUtc, x.NextAttemptAtUtc,
            x.LastError, x.PayloadJson
        }).SingleOrDefaultAsync(ct);
    return item is null ? Results.NotFound() : Results.Ok(item);
}).RequirePermission("Admin.Settlement.Process");

app.MapPost("/api/admin/outbox/messages/{id:long}/retry", async (
    long id,
    System.Security.Claims.ClaimsPrincipal user,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    HttpContext http,
    CancellationToken ct) =>
{
    var message = await db.OutboxMessages.SingleOrDefaultAsync(x => x.Id == id, ct);
    if (message is null) return Results.NotFound();
    if (message.Status != "DeadLetter")
        return Results.Conflict(new { message = "Only DeadLetter messages can be retried manually.", status = message.Status });

    var previousAttempts = message.Attempts;
    var previousError = message.LastError is { Length: > 500 } errorText ? errorText[..500] : message.LastError;
    message.RetryFromDeadLetter(DateTime.UtcNow);
    var actorUserId = CurrentUserId(user);
    var correlationId = http.TraceIdentifier;
    var audit = Marketplace.Domain.Auditing.AdminAuditEvent.Create(
        actorUserId,
        "Outbox.MessageRetried",
        "OutboxMessage",
        id.ToString(System.Globalization.CultureInfo.InvariantCulture),
        System.Text.Json.JsonSerializer.Serialize(new { message.MessageId, message.EventType, previousAttempts, previousError }),
        correlationId.Length <= 100 ? correlationId : correlationId[..100]);
    db.AdminAuditEvents.Add(audit);
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { id = message.Id, message.MessageId, status = message.Status, retriedAtUtc = DateTime.UtcNow, auditId = audit.Id });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/outbox/archive", async (
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    string? eventType,
    DateTime? fromUtc,
    DateTime? toUtc,
    CancellationToken ct,
    int page = 1,
    int pageSize = 25) =>
{
    if (fromUtc.HasValue && toUtc.HasValue && fromUtc.Value > toUtc.Value)
        throw new Marketplace.Domain.Common.DomainException("fromUtc must not be later than toUtc.");

    page = Math.Max(1, page);
    pageSize = Math.Clamp(pageSize == 0 ? 25 : pageSize, 1, 100);
    var eventTypeFilter = string.IsNullOrWhiteSpace(eventType) ? null : eventType.Trim();
    var skip = (page - 1) * pageSize;

    var total = await db.Database.SqlQuery<long>($@"
SELECT COUNT_BIG(*) AS Value
FROM dbo.OutboxMessageArchive
WHERE ({eventTypeFilter} IS NULL OR EventType LIKE N'%' + {eventTypeFilter} + N'%')
  AND ({fromUtc} IS NULL OR ArchivedAtUtc >= {fromUtc})
  AND ({toUtc} IS NULL OR ArchivedAtUtc <= {toUtc})")
        .SingleAsync(ct);

    var items = await db.Database.SqlQuery<OutboxArchiveListRow>($@"
SELECT Id, MessageId, EventType, Status, Attempts, OccurredAtUtc, ProcessedAtUtc,
       ArchivedAtUtc, LastError,
       CASE WHEN LEN(PayloadJson) > 500 THEN LEFT(PayloadJson, 500) ELSE PayloadJson END AS PayloadPreview
FROM dbo.OutboxMessageArchive
WHERE ({eventTypeFilter} IS NULL OR EventType LIKE N'%' + {eventTypeFilter} + N'%')
  AND ({fromUtc} IS NULL OR ArchivedAtUtc >= {fromUtc})
  AND ({toUtc} IS NULL OR ArchivedAtUtc <= {toUtc})
ORDER BY ArchivedAtUtc DESC, Id DESC
OFFSET {skip} ROWS FETCH NEXT {pageSize} ROWS ONLY")
        .ToListAsync(ct);

    return Results.Ok(new { page, pageSize, total, pageCount = (int)Math.Ceiling(total / (double)pageSize), items });
}).RequirePermission("Admin.Settlement.Process");

app.MapGet("/api/admin/outbox/archive/{id:long}", async (
    long id,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var item = await db.Database.SqlQuery<OutboxArchiveDetailRow>($@"
SELECT Id, MessageId, EventType, PayloadJson, OccurredAtUtc, ProcessedAtUtc,
       LockedUntilUtc, LockToken, NextAttemptAtUtc, Attempts, Status, LastError, ArchivedAtUtc
FROM dbo.OutboxMessageArchive
WHERE Id = {id}")
        .SingleOrDefaultAsync(ct);

    return item is null ? Results.NotFound() : Results.Ok(item);
}).RequirePermission("Admin.Settlement.Process");


app.MapGet("/api/admin/users", async (
    string? q, int? take,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var limit = Math.Clamp(take ?? 100, 1, 200);
    var query = db.Users.AsNoTracking();
    if (!string.IsNullOrWhiteSpace(q))
    {
        var term = q.Trim();
        query = query.Where(x => x.Mobile.Contains(term) || x.DisplayName.Contains(term) || (x.Email != null && x.Email.Contains(term)));
    }
    var items = await query.OrderByDescending(x => x.CreatedAtUtc).Take(limit)
        .Select(x => new { x.Id, x.Mobile, x.Email, x.DisplayName, x.IsActive, x.IsMobileVerified, x.CreatedAtUtc, x.LastLoginAtUtc })
        .ToListAsync(ct);
    return Results.Ok(items);
}).RequirePermission("Admin.Identity.Manage");

app.MapGet("/api/admin/sellers", async (
    string? q, int? take,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var limit = Math.Clamp(take ?? 100, 1, 200);
    var query = from seller in db.Sellers.AsNoTracking()
                join user in db.Users.AsNoTracking() on seller.UserId equals user.Id
                select new { Seller = seller, User = user };
    if (!string.IsNullOrWhiteSpace(q))
    {
        var term = q.Trim();
        query = query.Where(x => x.User.Mobile.Contains(term) || x.User.DisplayName.Contains(term));
    }
    var items = await query.OrderByDescending(x => x.Seller.CreatedAtUtc).Take(limit)
        .Select(x => new
        {
            id = x.Seller.Id, userId = x.User.Id, x.User.DisplayName, x.User.Mobile,
            status = x.Seller.Status.ToString(), x.Seller.CommissionRateBasisPoints,
            x.Seller.MinimumCommissionIRR, x.Seller.MaxStoreCount, x.Seller.CreatedAtUtc,
            storeCount = db.Stores.Count(store => store.SellerId == x.Seller.Id)
        }).ToListAsync(ct);
    return Results.Ok(items);
}).RequirePermission("Admin.Seller.Manage");

app.MapGet("/api/admin/categories", async (
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var items = await db.Categories.AsNoTracking().OrderBy(x => x.Path)
        .Select(x => new { x.Id, x.ParentCategoryId, x.Name, x.Slug, x.Path, x.IsActive, x.CreatedAtUtc })
        .ToListAsync(ct);
    return Results.Ok(items);
}).RequirePermission("Admin.Identity.Manage");

app.MapPost("/api/admin/users/{userId:long}/active", async (
    long userId, bool active, Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var user = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
    if (user is null) return Results.NotFound();
    if (active) user.Activate(); else user.Deactivate();
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
}).RequirePermission("Admin.Identity.Manage");

app.MapGet("/api/me/addresses", async (
    System.Security.Claims.ClaimsPrincipal user,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    var addresses = await db.CustomerAddresses.AsNoTracking()
        .Where(x => x.CustomerId == customerId)
        .Join(db.DeliveryCities.AsNoTracking(), address => address.CityId, city => city.Id,
            (address, city) => new { address.Id, address.CityId, CityName = city.Name, ProvinceName = city.ProvinceName,
                address.RecipientName, address.RecipientMobile, address.AddressLine, address.PostalCode,
                address.DeliveryNote, address.IsDefault, address.CreatedAtUtc, address.UpdatedAtUtc })
        .OrderByDescending(x => x.IsDefault).ThenByDescending(x => x.UpdatedAtUtc).ToListAsync(ct);
    return Results.Ok(addresses);
}).RequireAuthorization();

app.MapPost("/api/me/addresses", async (
    System.Security.Claims.ClaimsPrincipal user, CustomerAddressRequest request,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    Marketplace.Application.Abstractions.IIdGenerator ids, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    if (!await db.DeliveryCities.AnyAsync(x => x.Id == request.CityId && x.IsActive, ct))
        return Results.BadRequest(new { detail = "شهر مقصد معتبر یا فعال نیست." });
    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
    var existing = await db.CustomerAddresses.Where(x => x.CustomerId == customerId).ToListAsync(ct);
    if (existing.Count >= 30) return Results.BadRequest(new { detail = "حداکثر ۳۰ نشانی برای هر حساب مجاز است." });
    var makeDefault = request.IsDefault || existing.Count == 0;
    if (makeDefault) foreach (var item in existing) item.SetDefault(false);
    var address = Marketplace.Domain.Shipping.CustomerAddress.Create(
        await ids.NextAsync(ct), customerId, request.CityId, request.RecipientName, request.RecipientMobile,
        request.AddressLine, request.PostalCode, request.DeliveryNote, makeDefault);
    db.CustomerAddresses.Add(address);
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    return Results.Created($"/api/me/addresses/{address.Id}", new { address.Id });
}).RequireAuthorization();

app.MapPut("/api/me/addresses/{addressId:long}", async (
    System.Security.Claims.ClaimsPrincipal user, long addressId, CustomerAddressRequest request,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    var address = await db.CustomerAddresses.SingleOrDefaultAsync(x => x.Id == addressId && x.CustomerId == customerId, ct);
    if (address is null) return Results.NotFound();
    if (!await db.DeliveryCities.AnyAsync(x => x.Id == request.CityId && x.IsActive, ct))
        return Results.BadRequest(new { detail = "شهر مقصد معتبر یا فعال نیست." });
    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
    if (request.IsDefault)
        foreach (var other in await db.CustomerAddresses.Where(x => x.CustomerId == customerId && x.Id != addressId).ToListAsync(ct))
            other.SetDefault(false);
    address.Update(request.CityId, request.RecipientName, request.RecipientMobile, request.AddressLine, request.PostalCode, request.DeliveryNote, request.IsDefault);
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    return Results.NoContent();
}).RequireAuthorization();

app.MapPost("/api/me/addresses/{addressId:long}/default", async (
    System.Security.Claims.ClaimsPrincipal user, long addressId,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
    var address = await db.CustomerAddresses.SingleOrDefaultAsync(x => x.Id == addressId && x.CustomerId == customerId, ct);
    if (address is null) return Results.NotFound();
    foreach (var other in await db.CustomerAddresses.Where(x => x.CustomerId == customerId && x.Id != addressId).ToListAsync(ct))
        other.SetDefault(false);
    address.SetDefault(true);
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    return Results.NoContent();
}).RequireAuthorization();

app.MapDelete("/api/me/addresses/{addressId:long}", async (
    System.Security.Claims.ClaimsPrincipal user, long addressId,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var customerId = CurrentUserId(user);
    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
    var address = await db.CustomerAddresses.SingleOrDefaultAsync(x => x.Id == addressId && x.CustomerId == customerId, ct);
    if (address is null) return Results.NotFound();
    var wasDefault = address.IsDefault;
    db.CustomerAddresses.Remove(address);
    if (wasDefault)
    {
        var replacement = await db.CustomerAddresses.Where(x => x.CustomerId == customerId && x.Id != addressId)
            .OrderByDescending(x => x.UpdatedAtUtc).FirstOrDefaultAsync(ct);
        replacement?.SetDefault(true);
    }
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/me/profile", async (
    System.Security.Claims.ClaimsPrincipal user,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var userId = CurrentUserId(user);
    var account = await db.Users.AsNoTracking().Where(x => x.Id == userId)
        .Select(x => new { x.Id, x.Mobile, x.Email, x.DisplayName, x.IsMobileVerified })
        .SingleOrDefaultAsync(ct);
    return account is null ? Results.NotFound() : Results.Ok(account);
}).RequireAuthorization();

app.MapPut("/api/me/profile", async (
    System.Security.Claims.ClaimsPrincipal user,
    CustomerProfileUpdateRequest request,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db,
    CancellationToken ct) =>
{
    var userId = CurrentUserId(user);
    var account = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
    if (account is null) return Results.NotFound();
    account.UpdateProfile(request.DisplayName, request.Email);
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { account.Id, account.Mobile, account.Email, account.DisplayName, account.IsMobileVerified });
}).RequireAuthorization();

app.MapGet("/api/admin/stores/themes", async (
    string? q, string? themeCode, string? paletteCode, int? take, int? skip,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var limit = Math.Clamp(take ?? 50, 1, 100);
    var offset = Math.Max(skip ?? 0, 0);
    var query =
        from store in db.Stores.AsNoTracking()
        join seller in db.Sellers.AsNoTracking() on store.SellerId equals seller.Id
        select new
        {
            store.Id, store.Name, store.Slug,
            SellerId = seller.Id, SellerUserId = seller.UserId,
            store.Status, SellerStatus = seller.Status,
            store.ThemeCode, store.PaletteCode, store.ThemePrimaryColor, store.ThemeSecondaryColor,
            store.ThemeBackgroundColor, store.ThemeTextColor, store.ThemeFontCode, store.ThemeCornerStyle,
            ProductCount = db.Products.Count(product =>
                product.StoreId == store.Id && product.Status == Marketplace.Domain.Catalog.ProductStatus.Active),
            store.CreatedAtUtc
        };

    var term = q?.Trim();
    if (!string.IsNullOrWhiteSpace(term))
        query = query.Where(row => row.Name.Contains(term) || row.Slug.Contains(term));
    if (!string.IsNullOrWhiteSpace(themeCode))
        query = query.Where(row => row.ThemeCode == themeCode.Trim().ToLowerInvariant());
    if (!string.IsNullOrWhiteSpace(paletteCode))
        query = query.Where(row => row.PaletteCode == paletteCode.Trim().ToLowerInvariant());

    var total = await query.CountAsync(ct);
    var items = await query.OrderByDescending(row => row.CreatedAtUtc)
        .ThenBy(row => row.Id).Skip(offset).Take(limit).ToListAsync(ct);
    return Results.Ok(new { total, items });
}).RequirePermission("Admin.Identity.Manage");

app.MapPut("/api/admin/stores/{storeId:long}/theme", async (
    System.Security.Claims.ClaimsPrincipal user, long storeId, StoreThemeRequest request,
    Marketplace.Infrastructure.Persistence.MarketplaceDbContext db, CancellationToken ct) =>
{
    var store = await db.Stores.SingleOrDefaultAsync(x => x.Id == storeId, ct);
    if (store is null) return Results.NotFound();
    var previous = new { store.ThemeCode, store.PaletteCode, store.ThemePrimaryColor, store.ThemeSecondaryColor,
        store.ThemeBackgroundColor, store.ThemeTextColor, store.ThemeFontCode, store.ThemeCornerStyle };
    store.ConfigureTheme(request.ThemeCode);
    store.ConfigurePalette(request.PaletteCode);
    store.ConfigureAppearance(request.PrimaryColor, request.SecondaryColor, request.BackgroundColor,
        request.TextColor, request.FontCode, request.CornerStyle);
    await db.SaveChangesAsync(ct);
    return Results.Ok(new { store.Id, store.Name, previous, current = new {
        store.ThemeCode, store.PaletteCode, store.ThemePrimaryColor, store.ThemeSecondaryColor,
        store.ThemeBackgroundColor, store.ThemeTextColor, store.ThemeFontCode, store.ThemeCornerStyle },
        changedByUserId = CurrentUserId(user), changedAtUtc = DateTime.UtcNow });
}).RequirePermission("Admin.Identity.Manage");

app.Run();

public sealed record CustomerProfileUpdateRequest(string DisplayName, string? Email);
public sealed record CartItemRequest(long CustomerId,long SellerId,long StoreId,long ProductId,long VariantId,int Quantity,long? WarrantyId);
public sealed record CartQuantityRequest(int Quantity,long? WarrantyId);
public sealed record CheckoutRequest(Marketplace.Domain.Payments.PaymentProviderCode Provider,long DestinationCityId,string? CouponCode,string? RequestKey,long? AddressId = null);
public sealed record CustomerAddressRequest(long CityId,string RecipientName,string RecipientMobile,string AddressLine,string PostalCode,string? DeliveryNote,bool IsDefault);
public sealed record StoreThemeRequest(string ThemeCode, string PaletteCode, string? PrimaryColor = null, string? SecondaryColor = null, string? BackgroundColor = null, string? TextColor = null, string? FontCode = null, string? CornerStyle = null);
public sealed record StoreShippingCitiesRequest(long[] CityIds);
public sealed record StoreShippingRateRequest(long CityId, long ShippingFeeIRR, int MinDeliveryDays, int MaxDeliveryDays);
public sealed record StoreShippingRatesRequest(StoreShippingRateRequest[] Rates);
public sealed record ShipmentCreateRequest(string CarrierName, string TrackingNumber, string? TrackingUrl);
public sealed record ShipmentStatusUpdateRequest(Marketplace.Domain.Shipping.ShipmentStatus Status, string Description, string? Location, DateTime? OccurredAtUtc);
public sealed record SettlementRequest(long BankAccountId,long AmountIRR,string? RequestKey);
public sealed record SettlementReconciliationRequest(bool TransferCompleted,string? BankReference,string Note);
public sealed record RefundReconciliationRequest(bool TransferCompleted,string? BankReference,string Note);
public sealed record PaymentReconciliationRequest(string Action,string? BankReference,string Note);
public sealed record FinancialIntegrityReviewRequest(string Kind,string EntityKey,string Note);
public sealed record FinancialIntegrityCaseStatusRequest(string Kind,string EntityKey,string Status,string Note);
public sealed record FinancialLedgerFinding(long SellerId,string FindingType,string Bucket,long? CurrentBalanceIRR,long? LedgerBalanceAfterIRR,long? DifferenceIRR,long? LatestLedgerTransactionId,DateTime? LatestLedgerAtUtc,long? ActiveSettlementTotalIRR);
public sealed record FinancialOrderFlowFinding(string FindingType,long OrderId,long EntityId,long? RefundId,long? SellerId,long? ExpectedSellerId,long AmountIRR,long? ExpectedAmountIRR,long? CommissionAmountIRR,long? SellerAmountIRR,DateTime CreatedAtUtc);
public sealed record PaymentProviderConfigureRequest(bool IsEnabled,bool IsVisible,int SortOrder,string ConfigurationJson);
public sealed record SmsProviderConfigureRequest(bool IsEnabled,bool IsVisible,int SortOrder);
 public sealed record ProductReviewCreateRequest(long OrderId,int Rating,string Title,string Body);
 public sealed record ProductReviewModerationRequest(bool Approve,string? Note);
 public sealed record SmsAutomationConfigureRequest(bool AutomaticSmsEnabled,bool LowStockSmsEnabled);
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
public sealed record StockRequest(long Quantity,string? Reason=null);
 public sealed record StockThresholdRequest(long Threshold);
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

public partial class Program { }

public sealed class OutboxArchiveListRow
{
    public long Id { get; set; }
    public Guid MessageId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int Attempts { get; set; }
    public DateTime OccurredAtUtc { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
    public DateTime ArchivedAtUtc { get; set; }
    public string? LastError { get; set; }
    public string PayloadPreview { get; set; } = string.Empty;
}

public sealed class OutboxArchiveDetailRow
{
    public long Id { get; set; }
    public Guid MessageId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public string PayloadJson { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public DateTime ProcessedAtUtc { get; set; }
    public DateTime ArchivedAtUtc { get; set; }
    public DateTime? LockedUntilUtc { get; set; }
    public Guid? LockToken { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public int Attempts { get; set; }
    public string Status { get; set; } = string.Empty;
    public string? LastError { get; set; }
}
