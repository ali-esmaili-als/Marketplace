using Marketplace.Application;
using Marketplace.Infrastructure;

var builder=WebApplication.CreateBuilder(args);
builder.Services.AddMarketplaceApplication();
builder.Services.AddMarketplaceInfrastructure(builder.Configuration);
var app=builder.Build();

app.MapGet("/health",()=>Results.Ok(new{status="ok",utc=DateTime.UtcNow}));
app.MapPost("/api/orders/{orderId:long}/payment/succeeded",async(long orderId,PaymentSucceededRequest request,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    await service.PaymentSucceededAsync(orderId,request.Reference,ct);return Results.Ok();
});
app.MapPost("/api/orders/{orderId:long}/delivery/confirm",async(long orderId,DeliveryConfirmRequest request,Marketplace.Application.Orders.OrderLifecycleService service,CancellationToken ct)=>{
    await service.MarkDeliveredAsync(orderId,request.Reference,request.DeliveredAtUtc,request.ComplaintExpiresAtUtc,ct);return Results.Ok();
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

public sealed record PaymentSucceededRequest(string Reference);
public sealed record DeliveryConfirmRequest(string Reference,DateTime DeliveredAtUtc,DateTime ComplaintExpiresAtUtc);
public sealed record ComplaintRequest(long CustomerId,string Reason);
public sealed record ComplaintResolveRequest(bool CustomerWon,string Note);
public sealed record RefundRequest(Marketplace.Domain.Refunds.RefundReason Reason);