using System.Security.Claims;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Notification.Consumers;
using OrderFlow.Notification.Inbox;
using OrderFlow.Notification.Persistence;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// ADR 0005, spec 011: InMemory for component tests and dotnet run; Postgres for compose and integration tests.
var usePostgres = builder.Configuration["Persistence:Provider"] == "Postgres";

if (usePostgres)
{
    builder.Services.AddDbContext<NotificationDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Notifications")));
    builder.Services.AddScoped<IFakeInbox, EfFakeInbox>();
}
else
{
    builder.Services.AddSingleton<IFakeInbox, InMemoryFakeInbox>();
}

builder.AddOrderFlowServiceDefaults("notification", bus =>
{
    bus.AddConsumer<OrderPlacedConsumer>();
    bus.AddConsumer<OrderConfirmedConsumer>();
    bus.AddConsumer<OrderCancelledConsumer>();
    bus.AddConsumer<PaymentRefundedConsumer>();

    if (usePostgres)
    {
        // No outbox: nothing is published (spec 011, Q1). Copies of one event racing to save the
        // same message: the unique index rejects the second; retried with a fresh scope.
        bus.AddConfigureEndpointsCallback((_, _, endpoint) =>
            endpoint.UseMessageRetry(retry =>
            {
                retry.Handle<Exception>(IsRetryableDatabaseFailure);
                retry.Intervals(10, 50, 100, 250, 500, 1000, 2000);
            }));
    }
});

builder.AddOrderFlowAuthentication(); // spec 012, ADR 0008

var app = builder.Build();

if (usePostgres)
{
    await app.MigrateAsync<NotificationDbContext>(); // one copy at a time (spec 013)
}

app.MapOrderFlowDefaults();
// Spec 012: a customer reads only their own inbox.
app.MapGet("/inbox/{customerId}", async (string customerId, ClaimsPrincipal user, IFakeInbox inbox, CancellationToken cancellationToken) =>
        customerId == user.CustomerId()
            ? Results.Ok(await inbox.ForAsync(customerId, cancellationToken))
            : Results.Forbid())
    .RequireAuthorization()
    .WithName("getInbox");

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
