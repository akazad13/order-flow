using OrderFlow.OrderService.Api;
using OrderFlow.OrderService.Persistence;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddOrderFlowServiceDefaults("order");
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();

var app = builder.Build();

app.MapOrderFlowDefaults();
app.MapOrderEndpoints();

app.Run();
