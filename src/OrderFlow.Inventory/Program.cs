using OrderFlow.Inventory.Api;
using OrderFlow.Inventory.Consumers;
using OrderFlow.Inventory.Domain;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddOrderFlowServiceDefaults("inventory", bus => bus.AddConsumer<OrderPlacedConsumer>());
builder.Services.AddSingleton(new Catalog(CatalogSeed.Items));

var app = builder.Build();

app.MapOrderFlowDefaults();
app.MapStockEndpoints(app.Configuration);

app.Run();
