using MassTransit;
using Microsoft.EntityFrameworkCore;
using OrderFlow.OrderService.Api;
using OrderFlow.OrderService.Persistence;
using OrderFlow.OrderService.StateMachine;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// ADR 0005: InMemory for unit/component tests and dotnet run; Postgres for compose and integration tests.
var usePostgres = builder.Configuration["Persistence:Provider"] == "Postgres";

if (usePostgres)
{
    builder.Services.AddDbContext<OrderDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Orders")));
    builder.Services.AddScoped<IOrderRepository, EfOrderRepository>();
}
else
{
    builder.Services.AddSingleton<IOrderRepository, InMemoryOrderRepository>();
}

builder.AddOrderFlowServiceDefaults("order", bus =>
{
    if (usePostgres)
    {
        bus.AddEntityFrameworkOutbox<OrderDbContext>(outbox =>
        {
            outbox.UsePostgres();
            outbox.UseBusOutbox();
        });

        bus.AddSagaStateMachine<OrderStateMachine, OrderState, PostgresOrderStateDefinition>()
            .EntityFrameworkRepository(repository =>
            {
                repository.ConcurrencyMode = ConcurrencyMode.Pessimistic;
                repository.ExistingDbContext<OrderDbContext>();
                repository.UsePostgres();
            });
    }
    else
    {
        bus.AddSagaStateMachine<OrderStateMachine, OrderState, OrderStateDefinition>().InMemoryRepository();
    }

    // Spec 005: pushes final statuses to watching clients.
    bus.AddConsumer<OrderUpdatesConsumer>(consumer => consumer.UseMessageRetry(retry => retry.Intervals(100, 500, 1000)));
});

builder.Services.AddSignalR();

builder.Services.AddScoped<RecordStockHeldActivity>();
builder.Services.AddScoped<ConfirmOrderActivity>();
builder.Services.AddScoped<CancelForShortageActivity>();
builder.Services.AddScoped<CancelForPaymentActivity>();
builder.Services.AddScoped<CancelAtCustomerRequestActivity>();

var app = builder.Build();

if (usePostgres)
{
    // Safe with one instance per service only (ADR 0005).
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<OrderDbContext>().Database.MigrateAsync();
}

app.MapOrderFlowDefaults();
app.MapOrderEndpoints();
app.MapHub<OrderUpdatesHub>(OrderUpdatesHub.Path);

app.Run();
