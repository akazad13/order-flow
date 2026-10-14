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

    // Spec 005: pushes final statuses to watching clients. Each copy has a queue of its own,
    // deleted when the copy stops, so every copy hears every change and tells the browsers
    // connected to it (spec 013, Q1). The other consumers share one queue per service.
    bus.AddConsumer<OrderUpdatesConsumer>(consumer => consumer.UseMessageRetry(retry => retry.Intervals(100, 500, 1000)))
        .Endpoint(endpoint =>
        {
            endpoint.InstanceId = Guid.NewGuid().ToString("N")[..12];
            endpoint.Temporary = true;
        });
});

builder.Services.AddSignalR();
builder.AddOrderFlowAuthentication(); // spec 012, ADR 0008

builder.Services.AddScoped<RecordStockHeldActivity>();
builder.Services.AddScoped<ConfirmOrderActivity>();
builder.Services.AddScoped<CancelForShortageActivity>();
builder.Services.AddScoped<CancelForPaymentActivity>();
builder.Services.AddScoped<CancelAtCustomerRequestActivity>();

var app = builder.Build();

if (usePostgres)
{
    await app.MigrateAsync<OrderDbContext>(); // one copy at a time (spec 013)
}

app.MapOrderFlowDefaults();
app.MapOrderEndpoints();
app.MapHub<OrderUpdatesHub>(OrderUpdatesHub.Path);

app.Run();
