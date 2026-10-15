using Microsoft.Extensions.Options;
using MassTransit;
using OrderFlow.Contracts.Events;
using OrderFlow.OrderService.Persistence;

namespace OrderFlow.OrderService.StateMachine;

/// <summary>Stock is held: store the price Inventory fixed for the order.</summary>
public sealed class RecordStockHeldActivity(IOrderRepository orders) : IStateMachineActivity<OrderState, StockReserved>
{
    public async Task Execute(BehaviorContext<OrderState, StockReserved> context, IBehavior<OrderState, StockReserved> next)
    {
        var order = await orders.GetAsync(context.Message.OrderId, context.CancellationToken)
            ?? throw new InvalidOperationException($"Order {context.Message.OrderId} not found.");

        order.RecordStockHeld(context.Message.Total);
        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderState, StockReserved, TException> context, IBehavior<OrderState, StockReserved> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("record-stock-held");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

/// <summary>Payment succeeded: confirm the order and announce it.</summary>
public sealed class ConfirmOrderActivity(IOrderRepository orders, TimeProvider clock, IOptions<OrderOptions> options) : IStateMachineActivity<OrderState, PaymentSucceeded>
{
    public async Task Execute(BehaviorContext<OrderState, PaymentSucceeded> context, IBehavior<OrderState, PaymentSucceeded> next)
    {
        var order = await orders.GetAsync(context.Message.OrderId, context.CancellationToken)
            ?? throw new InvalidOperationException($"Order {context.Message.OrderId} not found.");

        // The deadline to cancel is fixed now, on the order and for the state machine (spec 022, R-2).
        order.Confirm(clock.GetUtcNow(), options.Value.CancelWindow);
        context.Saga.CancellableUntil = order.CancellableUntil;
        await context.Publish(new OrderConfirmed(order.Id, order.CustomerId, order.Total!.Value, clock.GetUtcNow()));
        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderState, PaymentSucceeded, TException> context, IBehavior<OrderState, PaymentSucceeded> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("confirm-order");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

/// <summary>Stock was short: cancel the order as OutOfStock (spec 002).</summary>
public sealed class CancelForShortageActivity(IOrderRepository orders, TimeProvider clock) : IStateMachineActivity<OrderState, StockRejected>
{
    public async Task Execute(BehaviorContext<OrderState, StockRejected> context, IBehavior<OrderState, StockRejected> next)
    {
        var detail = string.Join(" ", context.Message.Shortages.Select(s => $"Only {s.Available} of {s.Sku} available, {s.Requested} requested."));
        await OrderCancellation.Cancel(context, orders, clock, context.Message.OrderId, CancellationReasons.OutOfStock, detail);
        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderState, StockRejected, TException> context, IBehavior<OrderState, StockRejected> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("cancel-for-shortage");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

/// <summary>Payment was declined: cancel the order as PaymentDeclined (spec 003). Inventory releases the stock.</summary>
public sealed class CancelForPaymentActivity(IOrderRepository orders, TimeProvider clock) : IStateMachineActivity<OrderState, PaymentFailed>
{
    public async Task Execute(BehaviorContext<OrderState, PaymentFailed> context, IBehavior<OrderState, PaymentFailed> next)
    {
        await OrderCancellation.Cancel(context, orders, clock, context.Message.OrderId, CancellationReasons.PaymentDeclined, context.Message.Reason);
        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderState, PaymentFailed, TException> context, IBehavior<OrderState, PaymentFailed> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("cancel-for-payment");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

/// <summary>The customer asked to cancel a Pending order: cancel it as CustomerRequested (spec 006).</summary>
public sealed class CancelAtCustomerRequestActivity(IOrderRepository orders, TimeProvider clock) : IStateMachineActivity<OrderState, OrderCancellationRequested>
{
    public const string Detail = "Cancelled at the customer's request.";

    /// <summary>Staff cancel on the customer's behalf (spec 015); since spec 016 with a reason of its own.</summary>
    public const string StaffDetail = "Cancelled by our support team at the customer's request.";

    public async Task Execute(BehaviorContext<OrderState, OrderCancellationRequested> context, IBehavior<OrderState, OrderCancellationRequested> next)
    {
        var (reason, detail) = context.Message.RequestedBy == CancellationRequesters.Staff
            ? (CancellationReasons.StaffRequested, StaffDetail)
            : (CancellationReasons.CustomerRequested, Detail);
        await OrderCancellation.Cancel(context, orders, clock, context.Message.OrderId, reason, detail, context.Message.RequestedAt);
        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderState, OrderCancellationRequested, TException> context, IBehavior<OrderState, OrderCancellationRequested> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("cancel-at-customer-request");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}

internal static class OrderCancellation
{
    public static async Task Cancel(ConsumeContext context, IOrderRepository orders, TimeProvider clock, Guid orderId, string reason, string detail, DateTimeOffset? requestedAt = null)
    {
        var order = await orders.GetAsync(orderId, context.CancellationToken)
            ?? throw new InvalidOperationException($"Order {orderId} not found.");

        order.Cancel(reason, detail, requestedAt);

        // Version 2 only, never both (spec 016, R-2): readers of both would act twice.
        await context.Publish(new OrderCancelledV2(order.Id, order.CustomerId, reason, detail, clock.GetUtcNow()));
    }
}
