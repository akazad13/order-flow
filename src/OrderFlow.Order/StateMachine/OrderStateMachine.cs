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
///                       |                                  |
///                       +-> CancellationRequested -> Cancelled (customer, spec 006, from either state)
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
        // The API only accepts a request for an order it has stored, but the state machine may not
        // be tracking it yet (its OrderPlaced is still in flight): fault, and let the retry wait.
        Event(() => CancellationRequested, e =>
        {
            e.CorrelateById(m => m.Message.OrderId);
            e.OnMissingInstance(m => m.Fault());
        });
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

        // Repeats (spec 004) are ignored explicitly, so they are acknowledged instead of being
        // retried and faulted. Events that arrive early (not repeats) are still left unhandled
        // here on purpose: OrderStateDefinition retries them until their turn (ADR 0004).
        During(AwaitingStock,
            Ignore(OrderPlaced),
            When(StockReserved)
                .Activity(x => x.OfType<RecordStockHeldActivity>())
                .TransitionTo(AwaitingPayment),
            When(StockRejected)
                .Activity(x => x.OfType<CancelForShortageActivity>())
                .TransitionTo(Cancelled),
            When(CancellationRequested)
                .Activity(x => x.OfType<CancelAtCustomerRequestActivity>())
                .TransitionTo(Cancelled));

        During(AwaitingPayment,
            Ignore(OrderPlaced),
            Ignore(StockReserved),
            When(PaymentSucceeded)
                .Activity(x => x.OfType<ConfirmOrderActivity>())
                .TransitionTo(Confirmed),
            When(PaymentFailed)
                .Activity(x => x.OfType<CancelForPaymentActivity>())
                .TransitionTo(Cancelled),
            // Spec 006, AC-6: whichever of this and PaymentSucceeded is handled first wins; the
            // other is ignored by the final state. Payment refunds a charge it already took.
            When(CancellationRequested)
                .Activity(x => x.OfType<CancelAtCustomerRequestActivity>())
                .TransitionTo(Cancelled));

        // Status only moves forward (spec 001, R-4); final states ignore every earlier event.
        // Too late to cancel (spec 006, AC-2): the customer sees Confirmed.
        During(Confirmed,
            Ignore(OrderPlaced),
            Ignore(StockReserved),
            Ignore(PaymentSucceeded),
            Ignore(CancellationRequested));

        During(Cancelled,
            Ignore(OrderPlaced),
            Ignore(StockReserved),
            Ignore(StockRejected),
            Ignore(PaymentSucceeded),
            Ignore(PaymentFailed),
            Ignore(CancellationRequested));
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
    public Event<OrderCancellationRequested> CancellationRequested { get; private set; } = null!;
}
