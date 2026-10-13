using System.Globalization;
using System.Net.Http.Json;
using MassTransit.Testing;
using OrderFlow.Contracts.Events;
using OrderFlow.OrderService.Api;
using OrderFlow.OrderService.ComponentTests.Support;
using OrderFlow.OrderService.StateMachine;
using Reqnroll;
using Shouldly;

namespace OrderFlow.OrderService.ComponentTests.Steps;

[Binding]
public sealed class ConfirmOrderSteps(OrderServiceFactory service)
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

    private static decimal Money(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
