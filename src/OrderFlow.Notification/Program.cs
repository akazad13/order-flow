using OrderFlow.Notification.Consumers;
using OrderFlow.Notification.Inbox;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddOrderFlowServiceDefaults("notification", bus =>
{
    bus.AddConsumer<OrderPlacedConsumer>();
    bus.AddConsumer<OrderConfirmedConsumer>();
    bus.AddConsumer<OrderCancelledConsumer>();
});
builder.Services.AddSingleton<IFakeInbox, InMemoryFakeInbox>();

var app = builder.Build();

app.MapOrderFlowDefaults();
app.MapGet("/inbox/{customerId}", (string customerId, IFakeInbox inbox) => Results.Ok(inbox.For(customerId)))
    .WithName("getInbox");

app.Run();
