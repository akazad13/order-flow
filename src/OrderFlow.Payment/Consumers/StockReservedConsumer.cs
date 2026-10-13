using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Payment.Gateway;

namespace OrderFlow.Payment.Consumers;

/// <summary>
/// Payment is only ever requested here, after stock is held (spec 001, R-2).
/// </summary>
public sealed class StockReservedConsumer(IPaymentGateway gateway) : IConsumer<StockReserved>
{
    public async Task Consume(ConsumeContext<StockReserved> context)
    {
        var order = context.Message;
        var charge = gateway.Charge(order.OrderId, order.Total);

        await context.Publish(new PaymentSucceeded(order.OrderId, charge.PaymentId, charge.Amount, charge.ChargedAt));
    }
}
