using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Inventory.Domain;

namespace OrderFlow.Inventory.Consumers;

/// <summary>
/// Puts held stock back on sale (spec 003, AC-3). An order cancelled for a shortage held
/// nothing, and a repeated cancellation finds nothing left, so both are no-ops (R-1, R-2).
/// </summary>
public sealed class OrderCancelledConsumer(Catalog catalog, TimeProvider clock) : IConsumer<OrderCancelled>
{
    public async Task Consume(ConsumeContext<OrderCancelled> context)
    {
        var released = catalog.Release(context.Message.OrderId);
        if (released.Count == 0)
        {
            return;
        }

        await context.Publish(new StockReleased(
            context.Message.OrderId,
            released.Select(l => new StockReleasedLine(l.Sku, l.Quantity)).ToList(),
            clock.GetUtcNow()));
    }
}
