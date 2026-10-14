using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.Payment.Gateway;

namespace OrderFlow.Payment.Consumers;

/// <summary>
/// Payment is only ever requested here, after stock is held (spec 001, R-2).
/// </summary>
public sealed class StockReservedConsumer(IPaymentGateway gateway, TimeProvider clock) : IConsumer<StockReserved>
{
    public async Task Consume(ConsumeContext<StockReserved> context)
    {
        var order = context.Message;

        switch (gateway.ChargeOnce(order.OrderId, order.CustomerId, order.Total))
        {
            case null:
                // A repeat: the payment was already decided and announced (spec 004, R-1).
                break;

            case ChargeResult.Succeeded(var charge):
                await context.Publish(new PaymentSucceeded(order.OrderId, charge.PaymentId, charge.Amount, charge.ChargedAt));
                break;

            case ChargeResult.Declined(var reason):
                await context.Publish(new PaymentFailed(order.OrderId, reason, clock.GetUtcNow()));
                break;
        }
    }
}
