using OrderFlow.Payment.Consumers;
using OrderFlow.Payment.Gateway;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddOrderFlowServiceDefaults("payment", bus =>
{
    bus.AddConsumer<StockReservedConsumer>();
    bus.AddConsumer<OrderCancelledConsumer>();
});
builder.Services.AddSingleton<FakePaymentGateway>();
builder.Services.AddSingleton<IPaymentGateway>(sp => sp.GetRequiredService<FakePaymentGateway>());

var app = builder.Build();

app.MapOrderFlowDefaults();

// Test-only (contracts/http/payment.openapi.yaml): make a customer's payments fail (spec 003, Q1) or stall (spec 006, Q4).
if (app.Configuration.GetValue<bool>(TestEndpoints.EnableKey))
{
    app.MapPut("/test/declines/{customerId}", (string customerId, DeclineRule rule, FakePaymentGateway gateway) =>
    {
        if (string.IsNullOrWhiteSpace(rule.Reason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Reason is required."] });
        }

        gateway.DeclineFor(customerId, rule.Reason);
        return Results.NoContent();
    }).WithName("setDecline");

    // Spec 006, Q4: leave a customer's payments undecided, so their orders stay Pending.
    app.MapPut("/test/stalls/{customerId}", (string customerId, FakePaymentGateway gateway) =>
    {
        gateway.StallFor(customerId);
        return Results.NoContent();
    }).WithName("setStall");
}

app.Run();

public sealed record DeclineRule(string? Reason);

public static class TestEndpoints
{
    public const string EnableKey = "Payment:EnableTestEndpoints";
}
