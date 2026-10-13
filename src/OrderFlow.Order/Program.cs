using MassTransit;
using OrderFlow.OrderService.Api;
using OrderFlow.OrderService.Persistence;
using OrderFlow.OrderService.StateMachine;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddOrderFlowServiceDefaults("order", bus =>
    bus.AddSagaStateMachine<OrderStateMachine, OrderState, OrderStateDefinition>().InMemoryRepository());
builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
builder.Services.AddScoped<RecordStockHeldActivity>();
builder.Services.AddScoped<ConfirmOrderActivity>();

var app = builder.Build();

app.MapOrderFlowDefaults();
app.MapOrderEndpoints();

app.Run();
