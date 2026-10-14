using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Inventory.Domain;

namespace OrderFlow.Inventory.Consumers;

/// <summary>
/// Puts held stock back on sale (spec 003, AC-3). An order cancelled for a shortage held
/// nothing, and a repeated cancellation finds nothing left, so both are no-ops (R-1, R-2).
/// An order cancelled before its placement arrived is remembered as released (spec 006, AC-4).
/// Reads both versions of the event: v1 until it is retired (spec 016, ADR 0010).
/// </summary>
public sealed class OrderCancelledConsumer(ICatalogStore catalog, TimeProvider clock) : IConsumer<OrderCancelledV2>, IConsumer<OrderCancelled>
{
    public Task Consume(ConsumeContext<OrderCancelled> context) => Handle(context, context.Message.ToV2());

    public Task Consume(ConsumeContext<OrderCancelledV2> context) => Handle(context, context.Message);

    private async Task Handle(ConsumeContext context, OrderCancelledV2 order)
    {
        var released = await catalog.ReleaseAsync(order.OrderId, context.CancellationToken);
        if (released.Count == 0)
        {
            // Nothing to announce, but a first decision (Released) may need saving.
            await catalog.SaveChangesAsync(context.CancellationToken);
            return;
        }

        await context.Publish(new StockReleased(
            order.OrderId,
            released.Select(l => new StockReleasedLine(l.Sku, l.Quantity)).ToList(),
            clock.GetUtcNow()));

        // The release and its announcement commit together (spec 007, R-2).
        await catalog.SaveChangesAsync(context.CancellationToken);
    }
}
