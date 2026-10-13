using System.Globalization;
using System.Net.Http.Json;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Contracts.Events;
using OrderFlow.OrderService.Api;
using OrderFlow.OrderService.ComponentTests.Support;
using OrderFlow.OrderService.Domain;
using OrderFlow.OrderService.Persistence;
using OrderFlow.OrderService.StateMachine;
using Reqnroll;
using Shouldly;

namespace OrderFlow.OrderService.ComponentTests.Steps;

[Binding]
public sealed class OrderLifecycleSteps(OrderServiceFactory service)
{
    private OrderResponse? _placed;
    private decimal _reservedTotal;

    private ISagaStateMachineTestHarness<OrderStateMachine, OrderState> Saga =>
        service.Harness.GetSagaStateMachineHarness<OrderStateMachine, OrderState>();

    [Given("customer {string} has placed an order for {int} x {string}")]
    public async Task GivenCustomerHasPlacedAnOrder(string customerId, int quantity, string sku)
    {
        var response = await service.CreateClient().PostAsJsonAsync("/orders", new PlaceOrderRequest(customerId, [new OrderLineDto(sku, quantity)]));
        response.EnsureSuccessStatusCode();
        _placed = await response.Content.ReadFromJsonAsync<OrderResponse>();

        (await Saga.Consumed.Any<OrderPlaced>(m => m.Context.Message.OrderId == _placed!.OrderId)).ShouldBeTrue();
    }

    [When(@"^stock is reserved for that order with a total of (\d+\.\d{2})$")]
    public async Task WhenStockIsReserved(string total)
    {
        _reservedTotal = Money(total);
        await service.Harness.Bus.Publish(new StockReserved(
            _placed!.OrderId,
            _placed.CustomerId,
            _placed.Lines.Select(l => new StockReservedLine(l.Sku, l.Quantity, Money(total) / l.Quantity)).ToList(),
            Money(total),
            DateTimeOffset.UtcNow));

        (await Saga.Consumed.Any<StockReserved>(m => m.Context.Message.OrderId == _placed.OrderId)).ShouldBeTrue();
    }

    [When("payment succeeds for that order")]
    public async Task WhenPaymentSucceeds()
    {
        await service.Harness.Bus.Publish(new PaymentSucceeded(_placed!.OrderId, Guid.NewGuid(), _reservedTotal, DateTimeOffset.UtcNow));

        (await Saga.Consumed.Any<PaymentSucceeded>(m => m.Context.Message.OrderId == _placed.OrderId)).ShouldBeTrue();
    }

    [When(@"^payment succeeds before stock is reserved for that order, with a total of (\d+\.\d{2})$")]
    public async Task WhenPaymentOvertakesTheStockHold(string total)
    {
        _reservedTotal = Money(total);

        // RabbitMQ only orders messages per publisher; Payment and Inventory are different publishers.
        await service.Harness.Bus.Publish(new PaymentSucceeded(_placed!.OrderId, Guid.NewGuid(), _reservedTotal, DateTimeOffset.UtcNow));
        await service.Harness.Bus.Publish(new StockReserved(_placed.OrderId, _placed.CustomerId, [], _reservedTotal, DateTimeOffset.UtcNow));

        // Wait for the outcome (or the harness timeout); the Then steps assert it.
        await Saga.Exists(_placed.OrderId, machine => machine.Confirmed);
    }

    [Then(@"^the order has status ""(\w+)"" and a total of (\d+\.\d{2})$")]
    public async Task ThenTheOrderHasStatusAndTotal(string status, string total)
    {
        var order = await service.CreateClient().GetFromJsonAsync<OrderResponse>($"/orders/{_placed!.OrderId}");

        order!.Status.ShouldBe(status);
        order.Total.ShouldBe(Money(total));
    }

    [Then(@"^OrderConfirmed is published for that order with a total of (\d+\.\d{2})$")]
    public async Task ThenOrderConfirmedIsPublished(string total)
    {
        (await service.Harness.Published.Any<OrderConfirmed>(m => m.Context.Message.OrderId == _placed!.OrderId)).ShouldBeTrue();
        var confirmed = service.Harness.Published.Select<OrderConfirmed>(m => m.Context.Message.OrderId == _placed!.OrderId).Single().Context.Message;

        confirmed.Total.ShouldBe(Money(total));
        confirmed.CustomerId.ShouldBe(_placed!.CustomerId);
    }

    [Then("no OrderConfirmed is published")]
    public async Task ThenNoOrderConfirmedIsPublished()
    {
        (await service.Harness.Published.Any<OrderConfirmed>()).ShouldBeFalse();
    }

    [Given("an order for {int} x {string} is stored but not yet tracked")]
    public async Task GivenAnOrderIsStoredButNotYetTracked(int quantity, string sku)
    {
        // Saved by the API, but its OrderPlaced has not been processed by the state machine yet.
        var order = Order.Place("customer-1", [new OrderLine(sku, quantity)], TimeProvider.System).Order!;
        await service.Services.GetRequiredService<IOrderRepository>().AddAsync(order, CancellationToken.None);
        _placed = OrderResponse.From(order);
    }

    [When(@"^payment succeeds, then stock is reserved, then the order placement is processed, with a total of (\d+\.\d{2})$")]
    public async Task WhenEverythingArrivesBeforeOrderPlaced(string total)
    {
        var order = _placed!;
        await service.Harness.Bus.Publish(new PaymentSucceeded(order.OrderId, Guid.NewGuid(), Money(total), DateTimeOffset.UtcNow));
        await service.Harness.Bus.Publish(new StockReserved(order.OrderId, order.CustomerId, [], Money(total), DateTimeOffset.UtcNow));
        await service.Harness.Bus.Publish(new OrderPlaced(order.OrderId, order.CustomerId, order.Lines.Select(l => new OrderPlacedLine(l.Sku, l.Quantity)).ToList(), order.PlacedAt));

        await Saga.Exists(order.OrderId, machine => machine.Confirmed);
    }

    [When("stock is rejected for that order because {string} has {int} available")]
    public async Task WhenStockIsRejected(string sku, int available)
    {
        var requested = _placed!.Lines.Single(l => l.Sku == sku).Quantity;
        await service.Harness.Bus.Publish(new StockRejected(_placed.OrderId, _placed.CustomerId, [new StockShortage(sku, requested, available)], DateTimeOffset.UtcNow));

        await Saga.Exists(_placed.OrderId, machine => machine.Cancelled);
    }

    [When("payment fails for that order with reason {string}")]
    public async Task WhenPaymentFails(string reason)
    {
        await service.Harness.Bus.Publish(new PaymentFailed(_placed!.OrderId, reason, DateTimeOffset.UtcNow));

        await Saga.Exists(_placed.OrderId, machine => machine.Cancelled);
    }

    [When("payment fails before stock is reserved for that order")]
    public async Task WhenPaymentFailureOvertakesTheStockHold()
    {
        // RabbitMQ only orders messages per publisher; Payment and Inventory are different publishers.
        await service.Harness.Bus.Publish(new PaymentFailed(_placed!.OrderId, "Card declined", DateTimeOffset.UtcNow));
        await service.Harness.Bus.Publish(new StockReserved(_placed.OrderId, _placed.CustomerId, [], 37.00m, DateTimeOffset.UtcNow));

        await Saga.Exists(_placed.OrderId, machine => machine.Cancelled);
    }

    [Then("the order is cancelled with reason {string} and detail {string}")]
    public async Task ThenTheOrderIsCancelled(string reason, string detail)
    {
        var order = await service.CreateClient().GetFromJsonAsync<OrderResponse>($"/orders/{_placed!.OrderId}");

        order!.Status.ShouldBe("Cancelled");
        order.Cancellation.ShouldBe(new CancellationDto(reason, detail));
    }

    [Then("OrderCancelled is published for that order with reason {string}")]
    public async Task ThenOrderCancelledIsPublished(string reason)
    {
        (await service.Harness.Published.Any<OrderCancelled>(m => m.Context.Message.OrderId == _placed!.OrderId)).ShouldBeTrue();
        var cancelled = service.Harness.Published.Select<OrderCancelled>(m => m.Context.Message.OrderId == _placed!.OrderId).Single().Context.Message;

        cancelled.Reason.ShouldBe(reason);
        cancelled.CustomerId.ShouldBe(_placed!.CustomerId);
    }

    private static decimal Money(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
