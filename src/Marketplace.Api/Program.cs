using Microsoft.EntityFrameworkCore;
using Marketplace.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddDbContext<MarketplaceDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Marketplace")));
builder.Services.AddHealthChecks();
var app = builder.Build();
app.MapControllers();
app.MapHealthChecks("/health");
app.Run();
