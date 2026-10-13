using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Inventory.Domain;

namespace OrderFlow.Inventory.Consumers;

public sealed class OrderPlacedConsumer(
    Catalog catalog,
    TimeProvider clock,
    ILogger<OrderPlacedConsumer> logger) : IConsumer<OrderPlaced>
{
    public async Task Consume(ConsumeContext<OrderPlaced> context)
    {
        var order = context.Message;
        var result = catalog.TryReserve(order.Lines.Select(l => new RequestedLine(l.Sku, l.Quantity)).ToList());

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
                // Spec 002 replaces this with a StockRejected event; until then the order stays Pending.
                logger.LogWarning("Stock not held for order {OrderId}: {Reason}", order.OrderId, rejected.Reason);
                break;
        }
    }
}
