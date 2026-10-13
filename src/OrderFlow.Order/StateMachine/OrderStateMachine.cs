using MassTransit;
using OrderFlow.Contracts.Events;

namespace OrderFlow.OrderService.StateMachine;

public sealed class OrderState : SagaStateMachineInstance
{
    public Guid CorrelationId { get; set; }

    public string CurrentState { get; set; } = null!;
}

/// <summary>
/// Tracks one order across services (ADR 0004). Services react to each other's
/// events; this state machine only listens and is the single writer of order status.
///
///   OrderPlaced -> AwaitingStock -> StockReserved -> AwaitingPayment -> PaymentSucceeded -> Confirmed
/// </summary>
public sealed class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderPlaced, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => StockReserved, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentSucceeded, e => e.CorrelateById(m => m.Message.OrderId));

        Initially(
            When(OrderPlaced)
                .TransitionTo(AwaitingStock),
            // Plan 001 risk: StockReserved can overtake our own OrderPlaced on a busy queue.
            When(StockReserved)
                .Activity(x => x.OfType<RecordStockHeldActivity>())
                .TransitionTo(AwaitingPayment));

        During(AwaitingStock,
            When(StockReserved)
                .Activity(x => x.OfType<RecordStockHeldActivity>())
                .TransitionTo(AwaitingPayment));

        During(AwaitingPayment,
            Ignore(OrderPlaced),
            When(PaymentSucceeded)
                .Activity(x => x.OfType<ConfirmOrderActivity>())
                .TransitionTo(Confirmed));

        // Status only moves forward (R-4): a late OrderPlaced changes nothing.
        During(Confirmed,
            Ignore(OrderPlaced));
    }

    public State AwaitingStock { get; private set; } = null!;
    public State AwaitingPayment { get; private set; } = null!;
    public State Confirmed { get; private set; } = null!;

    public Event<OrderPlaced> OrderPlaced { get; private set; } = null!;
    public Event<StockReserved> StockReserved { get; private set; } = null!;
    public Event<PaymentSucceeded> PaymentSucceeded { get; private set; } = null!;
}
