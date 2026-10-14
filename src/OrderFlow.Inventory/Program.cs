using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.Inventory.Api;
using OrderFlow.Inventory.Consumers;
using OrderFlow.Inventory.Domain;
using OrderFlow.Inventory.Persistence;
using OrderFlow.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

// ADR 0005: InMemory for unit/component tests and dotnet run; Postgres for compose and integration tests.
var usePostgres = builder.Configuration["Persistence:Provider"] == "Postgres";

if (usePostgres)
{
    builder.Services.AddDbContext<InventoryDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Inventory")));
    builder.Services.AddScoped<ICatalogStore, EfCatalogStore>();
}
else
{
    builder.Services.AddSingleton(new Catalog(CatalogSeed.Items));
    builder.Services.AddScoped<ICatalogStore, InMemoryCatalogStore>();
}

builder.AddOrderFlowServiceDefaults("inventory", bus =>
{
    bus.AddConsumer<OrderPlacedConsumer>();
    bus.AddConsumer<OrderCancelledConsumer>();

    if (usePostgres)
    {
        bus.AddEntityFrameworkOutbox<InventoryDbContext>(outbox => outbox.UsePostgres());

        // Retry first, then the consumer outbox, so each attempt gets a fresh scope and DbContext.
        bus.AddConfigureEndpointsCallback((context, _, endpoint) =>
        {
            endpoint.UseMessageRetry(retry =>
            {
                // Two orders for the same item updated it at once (xmin), a serialization failure,
                // or two copies of one order inserting its decision (spec 004).
                retry.Handle<DbUpdateConcurrencyException>();
                retry.Handle<Exception>(IsRetryableDatabaseFailure);
                retry.Intervals(10, 50, 100, 250, 500, 1000, 2000);
            });
            endpoint.UseEntityFrameworkOutbox<InventoryDbContext>(context);
        });
    }
});

var app = builder.Build();

if (usePostgres)
{
    // One copy at a time (spec 013), so copies starting together cannot both seed the catalog.
    await app.MigrateAsync<InventoryDbContext>(async db =>
    {
        if (!await db.StockItems.AnyAsync())
        {
            db.StockItems.AddRange(CatalogSeed.Items.Select(i => new StockItemRow { Sku = i.Sku, Name = i.Name, UnitPrice = i.UnitPrice, Available = i.Available }));
            await db.SaveChangesAsync();
        }
    });
}

app.MapOrderFlowDefaults();
app.MapStockEndpoints(app.Configuration);

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
