using OrderFlow.Payment.Consumers;
using OrderFlow.Payment.Gateway;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddOrderFlowServiceDefaults("payment", bus => bus.AddConsumer<StockReservedConsumer>());
builder.Services.AddSingleton<FakePaymentGateway>();
builder.Services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<FakePaymentGateway>());

var app = builder.Build();

app.MapOrderFlowDefaults();

app.Run();
