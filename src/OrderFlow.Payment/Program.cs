using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Payment.Consumers;
using OrderFlow.Payment.Gateway;
using OrderFlow.Payment.Ledger;
using OrderFlow.Payment.Persistence;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// ADR 0005, spec 010: InMemory for component tests and dotnet run; Postgres for compose and integration tests.
var usePostgres = builder.Configuration["Persistence:Provider"] == "Postgres";

if (usePostgres)
{
    builder.Services.AddDbContext<PaymentDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Payments")));
    builder.Services.AddScoped<ITestSwitches, EfTestSwitches>(); // shared by every copy (spec 013)
    builder.Services.AddScoped<FakePaymentProvider>();
    builder.Services.AddScoped<IPaymentLedger, EfPaymentLedger>();
}
else
{
    builder.Services.AddSingleton<ITestSwitches, InMemoryTestSwitches>();
    builder.Services.AddSingleton<FakePaymentProvider>();
    builder.Services.AddSingleton<InMemoryPaymentLedger>();
    builder.Services.AddSingleton<IPaymentLedger>(sp => sp.GetRequiredService<InMemoryPaymentLedger>());
}

builder.AddOrderFlowServiceDefaults("payment", bus =>
{
    bus.AddConsumer<StockReservedConsumer>();
    bus.AddConsumer<OrderCancelledConsumer>();

    if (usePostgres)
    {
        bus.AddEntityFrameworkOutbox<PaymentDbContext>(outbox => outbox.UsePostgres());

        // Retry first, then the consumer outbox, so each attempt gets a fresh scope and DbContext.
        bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
        {
            endpoint.UseMessageRetry(retry =>
            {
                // Two copies deciding one order (primary key), two refunds of one charge (xmin),
                // or a serialization failure (spec 010, edge cases).
                retry.Handle<DbUpdateConcurrencyException>();
                retry.Handle<Exception>(IsRetryableDatabaseFailure);
                retry.Intervals(10, 50, 100, 250, 500, 1000, 2000);
            });
            endpoint.UseEntityFrameworkOutbox<PaymentDbContext>(context);
        });
    }
});

var app = builder.Build();

if (usePostgres)
{
    await app.MigrateAsync<PaymentDbContext>(); // one copy at a time (spec 013)
}

app.MapOrderFlowDefaults();

// Test-only (contracts/http/payment.openapi.yaml): make a customer's payments fail (spec 003, Q1) or stall (spec 006, Q4).
if (app.Configuration.GetValue<bool>(TestEndpoints.EnableKey))
{
    app.MapPut("/test/declines/{customerId}", async (string customerId, DeclineRule rule, ITestSwitches switches, CancellationToken cancellationToken) =>
    {
        if (string.IsNullOrWhiteSpace(rule.Reason))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["reason"] = ["Reason is required."] });
        }

        await switches.DeclineAsync(customerId, rule.Reason, cancellationToken);
        return Results.NoContent();
    }).WithName("setDecline");

    // Spec 006, Q4: leave a customer's payments undecided, so their orders stay Pending.
    app.MapPut("/test/stalls/{customerId}", async (string customerId, ITestSwitches switches, CancellationToken cancellationToken) =>
    {
        await switches.StallAsync(customerId, cancellationToken);
        return Results.NoContent();
    }).WithName("setStall");
}

app.Run();

static bool IsRetryableDatabaseFailure(Exception exception)
{
    for (var current = exception; current is not null; current = current.InnerException)
    {
        if (current is PostgresException
            {
                SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation,
            })
        {
            return true;
        }
    }

    return false;
}

public sealed record DeclineRule(string? Reason);

public static class TestEndpoints
{
    public const string EnableKey = "Payment:EnableTestEndpoints";
}
