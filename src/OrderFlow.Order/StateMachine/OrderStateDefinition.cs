using MassTransit;

namespace OrderFlow.OrderService.StateMachine;

/// <summary>
/// RabbitMQ only orders messages per publisher. StockReserved (from Inventory) and
/// PaymentSucceeded (from Payment) have different publishers, so PaymentSucceeded can
/// reach this queue first even though Payment only acts after StockReserved.
/// Found by the E2E stability run (about 1 failure in 40, then 7 in 100 with a
/// partitioner, which keeps arrival order and so cannot help).
///
/// An event that arrives before the state machine can handle it is retried with a short
/// back-off; meanwhile the earlier event is processed. Modelling each ordering explicitly
/// (and idempotency) is spec 004.
/// </summary>
public sealed class OrderStateDefinition : SagaDefinition<OrderState>
{
    protected override void ConfigureSaga(
        IReceiveEndpointConfigurator endpointConfigurator,
        ISagaConfigurator<OrderState> sagaConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry =>
        {
            retry.Handle<UnhandledEventException>();
            retry.Intervals(50, 100, 250, 500, 1000, 2000);
        });
    }
}
