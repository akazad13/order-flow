using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Inventory.Domain;

namespace OrderFlow.Inventory.Consumers;

/// <summary>
/// Puts held stock back on sale (spec 003, AC-3). An order cancelled for a shortage held
/// nothing, and a repeated cancellation finds nothing left, so both are no-ops (R-1, R-2).
/// An order cancelled before its placement arrived is remembered as released (spec 006, AC-4).
/// </summary>
public sealed class OrderCancelledConsumer(ICatalogStore catalog, TimeProvider clock) : IConsumer<OrderCancelled>
{
    public async Task Consume(ConsumeContext<OrderCancelled> context)
    {
        var released = await catalog.ReleaseAsync(context.Message.OrderId, context.CancellationToken);
        if (released.Count == 0)
        {
            // Nothing to announce, but a first decision (Released) may need saving.
            await catalog.SaveChangesAsync(context.CancellationToken);
            return;
        }

        await context.Publish(new StockReleased(
            context.Message.OrderId,
            released.Select(l => new StockReleasedLine(l.Sku, l.Quantity)).ToList(),
            clock.GetUtcNow()));

        // The release and its announcement commit together (spec 007, R-2).
        await catalog.SaveChangesAsync(context.CancellationToken);
    }
}
