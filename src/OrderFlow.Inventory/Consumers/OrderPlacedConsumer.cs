using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Inventory.Domain;

namespace OrderFlow.Inventory.Consumers;

public sealed class OrderPlacedConsumer(ICatalogStore catalog, TimeProvider clock) : IConsumer<OrderPlaced>
{
    public async Task Consume(ConsumeContext<OrderPlaced> context)
    {
        var order = context.Message;
        var result = await catalog.TryReserveAsync(
            order.OrderId,
            order.Lines.Select(l => new RequestedLine(l.Sku, l.Quantity)).ToList(),
            context.CancellationToken);

        switch (result)
        {
            case ReservationResult.Reserved reserved:
                await context.Publish(new StockReserved(
                    order.OrderId,
                    order.CustomerId,
                    reserved.Lines.Select(l => new StockReservedLine(l.Sku, l.Quantity, l.UnitPrice)).ToList(),
                    reserved.Total,
                    clock.GetUtcNow()));
                break;

            case ReservationResult.Rejected rejected:
                await context.Publish(new StockRejected(
                    order.OrderId,
                    order.CustomerId,
                    rejected.Shortages.Select(s => new StockShortage(s.Sku, s.Requested, s.Available)).ToList(),
                    clock.GetUtcNow()));
                break;
        }

        // The stock change and the announcement commit together (spec 007, R-2).
        await catalog.SaveChangesAsync(context.CancellationToken);
    }
}
