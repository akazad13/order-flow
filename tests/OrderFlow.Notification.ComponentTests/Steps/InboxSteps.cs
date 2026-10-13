using System.Net.Http.Json;
using OrderFlow.Contracts.Events;
using OrderFlow.Notification.ComponentTests.Support;
using OrderFlow.Notification.Consumers;
using OrderFlow.Notification.Inbox;
using Reqnroll;
using Shouldly;

namespace OrderFlow.Notification.ComponentTests.Steps;

[Binding]
public sealed class InboxSteps(NotificationServiceFactory service)
{
    private readonly Guid _orderId = Guid.NewGuid();
    private OrderPlaced? _placed;
    private OrderConfirmed? _confirmed;

    [When("an OrderPlaced event arrives for customer {string}")]
    public async Task WhenAnOrderPlacedEventArrives(string customerId)
    {
        // Starting the client starts the host, and with it the bus.
        _ = service.CreateClient();
        var orderPlaced = new OrderPlaced(Guid.NewGuid(), customerId, [new OrderPlacedLine("SKU-001", 2)], DateTimeOffset.UtcNow);

        await service.Harness.Bus.Publish(orderPlaced);

        var consumer = service.Harness.GetConsumerHarness<OrderPlacedConsumer>();
        (await consumer.Consumed.Any<OrderPlaced>(m => m.Context.Message.OrderId == orderPlaced.OrderId)).ShouldBeTrue();
    }

    [When("an OrderConfirmed event arrives for customer {string}")]
    public async Task WhenAnOrderConfirmedEventArrives(string customerId)
    {
        _ = service.CreateClient();
        var orderConfirmed = new OrderConfirmed(Guid.NewGuid(), customerId, 37.00m, DateTimeOffset.UtcNow);

        await service.Harness.Bus.Publish(orderConfirmed);

        var consumer = service.Harness.GetConsumerHarness<OrderConfirmedConsumer>();
        (await consumer.Consumed.Any<OrderConfirmed>(m => m.Context.Message.OrderId == orderConfirmed.OrderId)).ShouldBeTrue();
    }

    [Given("an order for customer {string} was placed and then confirmed")]
    public void GivenAnOrderWasPlacedAndThenConfirmed(string customerId)
    {
        var placedAt = DateTimeOffset.UtcNow;
        _placed = new OrderPlaced(_orderId, customerId, [new OrderPlacedLine("SKU-001", 2)], placedAt);
        _confirmed = new OrderConfirmed(_orderId, customerId, 37.00m, placedAt.AddSeconds(2));
    }

    [When("the confirmation reaches the Notification service before the order placement")]
    public async Task WhenTheConfirmationArrivesFirst()
    {
        _ = service.CreateClient();

        await service.Harness.Bus.Publish(_confirmed!);
        (await service.Harness.GetConsumerHarness<OrderConfirmedConsumer>().Consumed.Any<OrderConfirmed>(m => m.Context.Message.OrderId == _orderId)).ShouldBeTrue();

        await service.Harness.Bus.Publish(_placed!);
        (await service.Harness.GetConsumerHarness<OrderPlacedConsumer>().Consumed.Any<OrderPlaced>(m => m.Context.Message.OrderId == _orderId)).ShouldBeTrue();
    }

    [Then("the inbox of {string} lists, in order:")]
    public async Task ThenTheInboxListsInOrder(string customerId, DataTable table)
    {
        var inbox = await service.CreateClient().GetFromJsonAsync<List<InboxMessage>>($"/inbox/{customerId}");

        inbox!.Select(m => m.Text).ShouldBe(table.Rows.Select(r => r["message"]));
    }

    [Then("the inbox of {string} contains {string}")]
    public async Task ThenTheInboxContains(string customerId, string text)
    {
        var inbox = await service.CreateClient().GetFromJsonAsync<List<InboxMessage>>($"/inbox/{customerId}");

        inbox!.ShouldContain(m => m.Text == text);
    }
}
