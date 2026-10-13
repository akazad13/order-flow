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
///                       |                                  |
///                       +-> StockRejected -> Cancelled     +-> PaymentFailed -> Cancelled
/// </summary>
public sealed class OrderStateMachine : MassTransitStateMachine<OrderState>
{
    public OrderStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => OrderPlaced, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => StockReserved, e => e.CorrelateById(m => m.Message.OrderId));
        // Payment news can arrive before this order is tracked at all. MassTransit's default for
        // "no instance" is a silent discard, which left orders Pending forever (week 4 stress run).
        // Fault instead, so OrderStateDefinition's retry tries again once the order is tracked.
        Event(() => PaymentSucceeded, e =>
        {
            e.CorrelateById(m => m.Message.OrderId);
            e.OnMissingInstance(m => m.Fault());
        });
        Event(() => StockRejected, e => e.CorrelateById(m => m.Message.OrderId));
        Event(() => PaymentFailed, e =>
        {
            e.CorrelateById(m => m.Message.OrderId);
            e.OnMissingInstance(m => m.Fault());
        });

        Initially(
            When(OrderPlaced)
                .TransitionTo(AwaitingStock),
            // Plan 001 risk: StockReserved can overtake our own OrderPlaced on a busy queue.
            When(StockReserved)
                .Activity(x => x.OfType<RecordStockHeldActivity>())
                .TransitionTo(AwaitingPayment),
            When(StockRejected)
                .Activity(x => x.OfType<CancelForShortageActivity>())
                .TransitionTo(Cancelled));

        During(AwaitingStock,
            When(StockReserved)
                .Activity(x => x.OfType<RecordStockHeldActivity>())
                .TransitionTo(AwaitingPayment),
            When(StockRejected)
                .Activity(x => x.OfType<CancelForShortageActivity>())
                .TransitionTo(Cancelled));

        During(AwaitingPayment,
            Ignore(OrderPlaced),
            When(PaymentSucceeded)
                .Activity(x => x.OfType<ConfirmOrderActivity>())
                .TransitionTo(Confirmed),
            When(PaymentFailed)
                .Activity(x => x.OfType<CancelForPaymentActivity>())
                .TransitionTo(Cancelled));

        // Status only moves forward (R-4): a late OrderPlaced changes nothing.
        During(Confirmed,
            Ignore(OrderPlaced));

        During(Cancelled,
            Ignore(OrderPlaced));
    }

    public State AwaitingStock { get; private set; } = null!;
    public State AwaitingPayment { get; private set; } = null!;
    public State Confirmed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    public Event<OrderPlaced> OrderPlaced { get; private set; } = null!;
    public Event<StockReserved> StockReserved { get; private set; } = null!;
    public Event<PaymentSucceeded> PaymentSucceeded { get; private set; } = null!;
    public Event<StockRejected> StockRejected { get; private set; } = null!;
    public Event<PaymentFailed> PaymentFailed { get; private set; } = null!;
}
