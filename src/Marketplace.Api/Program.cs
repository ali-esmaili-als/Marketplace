using Marketplace.Application;
using Marketplace.Infrastructure;
using Marketplace.Api.Auth;
using Marketplace.Api.DTOs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
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
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationPolicyProvider, PermissionPolicyProvider>();
builder.Services.AddScoped<Microsoft.AspNetCore.Authorization.IAuthorizationHandler, PermissionHandler>();

var app=builder.Build();

long CurrentUserId(System.Security.Claims.ClaimsPrincipal user)
    => long.TryParse(user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value, out var id) ? id : throw new UnauthorizedAccessException();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health",()=>Results.Ok(new{status="ok",utc=DateTime.UtcNow}));

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

app.MapDelete("/api/admin/users/{userId:long}/rules",async(long userId,UserRuleRequest request,Marketplace.Application.Identity.IdentityAdminService service,CancellationToken ct)=>{
    await service.RevokeRuleAsync(userId,request.Code,ct); return Results.NoContent();
}).RequirePermission("Admin.Identity.Manage");

app.MapPost("/api/admin/users/{userId:long}/roles",async(long userId,UserRoleRequest request,Marketplace.Application.Identity.IdentityAdminService service,CancellationToken ct)=>{
    await service.AssignRoleAsync(userId,request.RoleName,ct); return Results.NoContent();
}).RequirePermission("Admin.Identity.Manage");

app.MapDelete("/api/admin/users/{userId:long}/roles",async(long userId,UserRoleRequest request,Marketplace.Application.Identity.IdentityAdminService service,CancellationToken ct)=>{
    await service.RevokeRoleAsync(userId,request.RoleName,ct); return Results.NoContent();
}).RequirePermission("Admin.Identity.Manage");

app.MapPost("/api/auth/login",async(LoginRequest request,Marketplace.Application.Identity.AuthenticationService service,CancellationToken ct)=>
{
    var result=await service.LoginAsync(request.Mobile,request.Password,ct);
    return Results.Ok(result);
});

app.MapGet("/api/cart/items",async(long customerId,Marketplace.Application.Cart.CartService service,CancellationToken ct)=>{
    var items=await service.GetItemsAsync(customerId,ct); return Results.Ok(items);
}).RequirePermission("Cart.Read");

app.MapPost("/api/cart/items",async(CartItemRequest request,Marketplace.Application.Cart.CartService service,CancellationToken ct)=>{
    await service.AddItemAsync(request.CustomerId,request.SellerId,request.StoreId,request.ProductId,request.VariantId,request.Quantity,request.WarrantyId,ct);
    return Results.Ok();
}).RequirePermission("Cart.Read");

app.MapDelete("/api/cart/items/{variantId:long}",async(long variantId,long customerId,long? warrantyId,Marketplace.Application.Cart.CartService service,CancellationToken ct)=>{
    await service.RemoveItemAsync(customerId,variantId,warrantyId,ct); return Results.Ok();
}).RequirePermission("Cart.Read");

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

app.MapGet("/api/admin/payment-providers",async(Marketplace.Application.Payments.PaymentProviderSettingsService service,CancellationToken ct)=>
    Results.Ok(await service.GetAllAsync(ct))).RequirePermission("Admin.PaymentProviders.Read");

app.MapPut("/api/admin/payment-providers/{provider}",async(Marketplace.Domain.Payments.PaymentProviderCode provider,PaymentProviderConfigureRequest request,Marketplace.Application.Payments.PaymentProviderSettingsService service,CancellationToken ct)=>{
    await service.ConfigureAsync(provider,request.IsEnabled,request.IsVisible,request.SortOrder,request.ConfigurationJson,ct);
    return Results.NoContent();
}).RequirePermission("Admin.PaymentProviders.Configure");

app.MapPost("/api/settlements",async(System.Security.Claims.ClaimsPrincipal user,SettlementRequest request,Marketplace.Application.Settlements.SettlementService service,CancellationToken ct)=>{
    var result=await service.RequestAsync(CurrentUserId(user),request.BankAccountId,request.AmountIRR,ct); return Results.Ok(result);
}).RequirePermission("Seller.Settlement.Request");

app.MapPost("/api/settlements/{settlementId:long}/process",async(long settlementId,Marketplace.Application.Settlements.SettlementService service,CancellationToken ct)=>{
    var result=await service.ProcessAsync(settlementId,ct);
    return result.Status=="Completed" ? Results.Ok(result) : Results.BadRequest(result);
}).RequirePermission("Admin.Settlement.Process");

app.MapPost("/api/orders/checkout",async(CheckoutRequest request,Marketplace.Application.Orders.OrderCreationService service,CancellationToken ct)=>{
    var result=await service.CheckoutAsync(request.CustomerId,request.Provider,request.DestinationCityId,ct); return Results.Ok(result);
}).RequirePermission("Order.Create");

app.MapPost("/api/payments/{paymentId:long}/verify",async(long paymentId,PaymentVerifyRequest request,Marketplace.Application.Orders.PaymentVerificationService service,CancellationToken ct)=>{
    var result=await service.VerifyAsync(paymentId,request.Authority,ct);
    return result.Paid ? Results.Ok(result) : Results.BadRequest(result);
}).RequirePermission("Order.Create");

app.MapPost("/api/orders/{orderId:long}/delivery/ready",async(long orderId,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    await service.MarkReadyForDeliveryAsync(orderId,ct);return Results.Ok();
}).RequirePermission("Order.Delivery.Confirm");

app.MapPost("/api/orders/{orderId:long}/delivery/confirm",async(long orderId,DeliveryConfirmRequest request,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    await service.MarkDeliveredAsync(orderId,request.Reference,request.DeliveredAtUtc,request.ComplaintExpiresAtUtc,ct); return Results.Ok();
}).RequirePermission("Order.Delivery.Confirm");

app.MapPost("/api/orders/{orderId:long}/delivery/expire",async(long orderId,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    await service.ExpireDeliveryAsync(orderId,DateTime.UtcNow,ct);return Results.Ok();
}).RequirePermission("Order.Delivery.Confirm");

app.MapPost("/api/orders/{orderId:long}/complaints",async(long orderId,ComplaintRequest request,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    var id=await service.OpenComplaintAsync(orderId,request.CustomerId,request.Reason,ct);return Results.Ok(new{id});
}).RequirePermission("Order.Create");

app.MapPost("/api/complaints/{complaintId:long}/resolve",async(long complaintId,ComplaintResolveRequest request,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    await service.ResolveComplaintAsync(complaintId,request.CustomerWon,request.Note,ct);return Results.Ok();
}).RequirePermission("Complaint.Resolve");

app.MapPost("/api/orders/{orderId:long}/refund",async(long orderId,RefundRequest request,Marketplace.Application.Orders.RefundService service,CancellationToken ct)=>{
    await service.ProcessAsync(orderId,request.Reason,ct);return Results.Ok();
}).RequirePermission("Order.Create");

app.Run();

public sealed record CartItemRequest(long CustomerId,long SellerId,long StoreId,long ProductId,long VariantId,int Quantity,long? WarrantyId);
public sealed record CheckoutRequest(long CustomerId,Marketplace.Domain.Payments.PaymentProviderCode Provider,long DestinationCityId);
public sealed record StoreShippingCitiesRequest(long[] CityIds);
public sealed record SettlementRequest(long BankAccountId,long AmountIRR);
public sealed record PaymentProviderConfigureRequest(bool IsEnabled,bool IsVisible,int SortOrder,string ConfigurationJson);
public sealed record PaymentVerifyRequest(string Authority);
public sealed record DeliveryConfirmRequest(string Reference,DateTime DeliveredAtUtc,DateTime ComplaintExpiresAtUtc);
public sealed record ComplaintRequest(long CustomerId,string Reason);
public sealed record ComplaintResolveRequest(bool CustomerWon,string Note);
public sealed record RefundRequest(Marketplace.Domain.Refunds.RefundReason Reason);