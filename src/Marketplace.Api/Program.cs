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
app.UseRateLimiter();

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
            s.Id, s.Name, s.Slug, s.Description, s.CreatedAtUtc,
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
        select new { s.Id, s.Name, s.Slug, s.Description, s.CreatedAtUtc }
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
            p.Id, p.StoreId, SellerId = s.SellerId, StoreName = s.Name, StoreSlug = s.Slug,
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

app.MapPost("/api/orders/checkout",async(System.Security.Claims.ClaimsPrincipal user,CheckoutRequest request,Marketplace.Application.Orders.OrderCreationService service,CancellationToken ct)=>{
    if(string.IsNullOrWhiteSpace(request.RequestKey)||request.RequestKey.Length<16||request.RequestKey.Length>64) return Results.BadRequest(new { detail="A valid checkout request key is required." });
    var result=await service.CheckoutAsync(CurrentUserId(user),request.Provider,request.DestinationCityId,request.CouponCode,request.RequestKey,ct); return Results.Ok(result);
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
                bt.OrderId == refund.OrderId
                && bt.SellerId == order.SellerId
                && bt.Type == Marketplace.Domain.Finance.BalanceTransactionType.Refund)
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
public sealed record CartQuantityRequest(int Quantity,long? WarrantyId);
public sealed record CheckoutRequest(Marketplace.Domain.Payments.PaymentProviderCode Provider,long DestinationCityId,string? CouponCode,string? RequestKey);
public sealed record StoreShippingCitiesRequest(long[] CityIds);
public sealed record SettlementRequest(long BankAccountId,long AmountIRR,string? RequestKey);
public sealed record SettlementReconciliationRequest(bool TransferCompleted,string? BankReference,string Note);
public sealed record RefundReconciliationRequest(bool TransferCompleted,string? BankReference,string Note);
public sealed record PaymentReconciliationRequest(string Action,string? BankReference,string Note);
public sealed record FinancialIntegrityReviewRequest(string Kind,string EntityKey,string Note);
public sealed record FinancialLedgerFinding(long SellerId,string FindingType,string Bucket,long? CurrentBalanceIRR,long? LedgerBalanceAfterIRR,long? DifferenceIRR,long? LatestLedgerTransactionId,DateTime? LatestLedgerAtUtc,long? ActiveSettlementTotalIRR);
public sealed record FinancialOrderFlowFinding(string FindingType,long OrderId,long EntityId,long? RefundId,long? SellerId,long? ExpectedSellerId,long AmountIRR,long? ExpectedAmountIRR,long? CommissionAmountIRR,long? SellerAmountIRR,DateTime CreatedAtUtc);
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