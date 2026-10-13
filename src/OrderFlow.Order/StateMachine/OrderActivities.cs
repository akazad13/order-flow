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
public sealed class ConfirmOrderActivity(IOrderRepository orders, TimeProvider clock) : IStateMachineActivity<OrderState, PaymentSucceeded>
{
    public async Task Execute(BehaviorContext<OrderState, PaymentSucceeded> context, IBehavior<OrderState, PaymentSucceeded> next)
    {
        var order = await orders.GetAsync(context.Message.OrderId, context.CancellationToken)
            ?? throw new InvalidOperationException($"Order {context.Message.OrderId} not found.");

        order.Confirm();
        await context.Publish(new OrderConfirmed(order.Id, order.CustomerId, order.Total!.Value, clock.GetUtcNow()));
        await next.Execute(context);
    }

    public Task Faulted<TException>(BehaviorExceptionContext<OrderState, PaymentSucceeded, TException> context, IBehavior<OrderState, PaymentSucceeded> next)
        where TException : Exception => next.Faulted(context);

    public void Probe(ProbeContext context) => context.CreateScope("confirm-order");

    public void Accept(StateMachineVisitor visitor) => visitor.Visit(this);
}
