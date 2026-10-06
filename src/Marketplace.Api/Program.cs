using Marketplace.Application;
using Marketplace.Infrastructure;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddMarketplaceApplication();
builder.Services.AddMarketplaceInfrastructure(builder.Configuration);
var app=builder.Build();

app.MapGet("/health",()=>Results.Ok(new{status="ok",utc=DateTime.UtcNow}));

app.MapPost("/api/cart/items",async(CartItemRequest request,Marketplace.Application.Cart.CartService service,CancellationToken ct)=>{
    await service.AddItemAsync(request.CustomerId,request.SellerId,request.StoreId,request.ProductId,request.VariantId,request.Quantity,request.WarrantyId,ct);
    return Results.Ok();
});
app.MapDelete("/api/cart/items/{variantId:long}",async(long variantId,long customerId,long? warrantyId,Marketplace.Application.Cart.CartService service,CancellationToken ct)=>{
    await service.RemoveItemAsync(customerId,variantId,warrantyId,ct);
    return Results.Ok();
});
app.MapGet("/api/cart",async(long customerId,Marketplace.Application.Cart.CartService service,CancellationToken ct)=>{
    var items=await service.GetItemsAsync(customerId,ct);
    return Results.Ok(items);
});
app.MapGet("/api/shipping/cities",async(Marketplace.Application.Shipping.ShippingCoverageService service,CancellationToken ct)=>
    Results.Ok(await service.GetCitiesAsync(ct)));

app.MapGet("/api/stores/{storeId:long}/shipping-cities",async(long storeId,Marketplace.Application.Shipping.ShippingCoverageService service,CancellationToken ct)=>
    Results.Ok(await service.GetStoreCitiesAsync(storeId,ct)));

app.MapPut("/api/stores/{storeId:long}/shipping-cities",async(long storeId,StoreShippingCitiesRequest request,Marketplace.Application.Shipping.ShippingCoverageService service,CancellationToken ct)=>{
    await service.ConfigureStoreCitiesAsync(storeId,request.CityIds,ct);
    return Results.NoContent();
});

app.MapGet("/api/payments/providers",async(Marketplace.Application.Abstractions.IPaymentProviderSettings settings,CancellationToken ct)=>
    Results.Ok(await settings.GetAvailableAsync(ct)));

app.MapGet("/api/admin/payment-providers",async(Marketplace.Application.Payments.PaymentProviderSettingsService service,CancellationToken ct)=>
    Results.Ok(await service.GetAllAsync(ct)));
app.MapPut("/api/admin/payment-providers/{provider}",async(Marketplace.Domain.Payments.PaymentProviderCode provider,PaymentProviderConfigureRequest request,Marketplace.Application.Payments.PaymentProviderSettingsService service,CancellationToken ct)=>{
    await service.ConfigureAsync(provider,request.IsEnabled,request.IsVisible,request.SortOrder,request.ConfigurationJson,ct);
    return Results.NoContent();
});

app.MapPost("/api/settlements",async(SettlementRequest request,Marketplace.Application.Settlements.SettlementService service,CancellationToken ct)=>{
    var result=await service.RequestAsync(request.SellerId,request.BankAccountId,request.AmountIRR,ct);
    return Results.Ok(result);
});
app.MapPost("/api/settlements/{settlementId:long}/process",async(long settlementId,Marketplace.Application.Settlements.SettlementService service,CancellationToken ct)=>{
    var result=await service.ProcessAsync(settlementId,ct);
    return result.Status=="Completed" ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/orders/checkout",async(CheckoutRequest request,Marketplace.Application.Orders.OrderCreationService service,CancellationToken ct)=>{
    var result=await service.CheckoutAsync(request.CustomerId,request.Provider,request.DestinationCityId,ct);
    return Results.Ok(result);
});

app.MapPost("/api/payments/{paymentId:long}/verify",async(long paymentId,PaymentVerifyRequest request,Marketplace.Application.Orders.PaymentVerificationService service,CancellationToken ct)=>{
    var result=await service.VerifyAsync(paymentId,request.Authority,ct);
    return result.Paid ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/orders/{orderId:long}/delivery/ready",async(long orderId,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    await service.MarkReadyForDeliveryAsync(orderId,ct);return Results.Ok();
});

app.MapPost("/api/orders/{orderId:long}/delivery/confirm",async(long orderId,DeliveryConfirmRequest request,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    await service.MarkDeliveredAsync(orderId,request.Reference,request.DeliveredAtUtc,request.ComplaintExpiresAtUtc,ct);
    return Results.Ok();
});
app.MapPost("/api/orders/{orderId:long}/delivery/expire",async(long orderId,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    await service.ExpireDeliveryAsync(orderId,DateTime.UtcNow,ct);return Results.Ok();
});
app.MapPost("/api/orders/{orderId:long}/complaints",async(long orderId,ComplaintRequest request,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    var id=await service.OpenComplaintAsync(orderId,request.CustomerId,request.Reason,ct);return Results.Ok(new{id});
});
app.MapPost("/api/complaints/{complaintId:long}/resolve",async(long complaintId,ComplaintResolveRequest request,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    await service.ResolveComplaintAsync(complaintId,request.CustomerWon,request.Note,ct);return Results.Ok();
});
app.MapPost("/api/orders/{orderId:long}/refund",async(long orderId,RefundRequest request,Marketplace.Application.Orders.RefundService service,CancellationToken ct)=>{
    await service.ProcessAsync(orderId,request.Reason,ct);return Results.Ok();
});

app.Run();

public sealed record CartItemRequest(long CustomerId,long SellerId,long StoreId,long ProductId,long VariantId,int Quantity,long? WarrantyId);
public sealed record CheckoutRequest(long CustomerId,Marketplace.Domain.Payments.PaymentProviderCode Provider,long DestinationCityId);
public sealed record StoreShippingCitiesRequest(long[] CityIds);
public sealed record SettlementRequest(long SellerId,long BankAccountId,long AmountIRR);
public sealed record PaymentProviderConfigureRequest(bool IsEnabled,bool IsVisible,int SortOrder,string ConfigurationJson);
public sealed record PaymentVerifyRequest(string Authority);
public sealed record DeliveryConfirmRequest(string Reference,DateTime DeliveredAtUtc,DateTime ComplaintExpiresAtUtc);
public sealed record ComplaintRequest(long CustomerId,string Reason);
public sealed record ComplaintResolveRequest(bool CustomerWon,string Note);
public sealed record RefundRequest(Marketplace.Domain.Refunds.RefundReason Reason);
