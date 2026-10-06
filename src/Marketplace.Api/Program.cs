using Marketplace.Application;
using Marketplace.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddControllers();
builder.Services.AddAuthorization();
builder.Services.AddMarketplaceApplication();
builder.Services.AddMarketplaceInfrastructure(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler();

app.UseHttpsRedirection();
app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
