using System.Net.Http.Json;
using OrderFlow.Contracts.Events;
using OrderFlow.Notification.ComponentTests.Support;
using OrderFlow.Notification.Consumers;
using OrderFlow.Notification.Inbox;
using OrderFlow.Testing;
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
        await Eventually.True(() => consumer.Consumed.Select<OrderPlaced>(m => m.Context.Message.OrderId == orderPlaced.OrderId).Any(), "OrderPlaced (Consumed)");
    }

    [When("an OrderConfirmed event arrives for customer {string}")]
    public async Task WhenAnOrderConfirmedEventArrives(string customerId)
    {
        _ = service.CreateClient();
        var orderConfirmed = new OrderConfirmed(Guid.NewGuid(), customerId, 37.00m, DateTimeOffset.UtcNow);

        await service.Harness.Bus.Publish(orderConfirmed);

        var consumer = service.Harness.GetConsumerHarness<OrderConfirmedConsumer>();
        await Eventually.True(() => consumer.Consumed.Select<OrderConfirmed>(m => m.Context.Message.OrderId == orderConfirmed.OrderId).Any(), "OrderConfirmed (Consumed)");
    }

    [When("an OrderCancelled event with reason {string} arrives for customer {string}")]
    public async Task WhenAnOrderCancelledEventArrives(string reason, string customerId)
    {
        _ = service.CreateClient();
        await service.Harness.Bus.Publish(new OrderCancelled(_orderId, customerId, reason, "detail", DateTimeOffset.UtcNow));

        var consumer = service.Harness.GetConsumerHarness<OrderCancelledConsumer>();
        await Eventually.True(() => consumer.Consumed.Select<OrderCancelled>(m => m.Context.Message.OrderId == _orderId).Any(), "OrderCancelled (Consumed)");
    }

    [When(@"^a PaymentRefunded event of (\d+\.\d{2}) arrives for customer ""([^""]+)""$")]
    public async Task WhenAPaymentRefundedEventArrives(string amount, string customerId)
    {
        _ = service.CreateClient();
        await service.Harness.Bus.Publish(new PaymentRefunded(_orderId, customerId, Guid.NewGuid(), decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), DateTimeOffset.UtcNow));

        // Any consumer: the scenario fails on the inbox if nothing handles it.
        await Eventually.True(() => service.Harness.Consumed.Select<PaymentRefunded>(m => m.Context.Message.OrderId == _orderId).Any(), "PaymentRefunded (Consumed)");
    }

    [When("an OrderConfirmed event for customer {string} arrives twice")]
    public async Task WhenAnOrderConfirmedEventArrivesTwice(string customerId)
    {
        _ = service.CreateClient();
        var confirmed = new OrderConfirmed(_orderId, customerId, 37.00m, DateTimeOffset.UtcNow);

        // Two real repeats: same content, new message IDs.
        await service.Harness.Bus.Publish(confirmed);
        await service.Harness.Bus.Publish(confirmed);

        var consumer = service.Harness.GetConsumerHarness<OrderConfirmedConsumer>();
        await Eventually.True(
            () => consumer.Consumed.Select<OrderConfirmed>(m => m.Context.Message.OrderId == _orderId).Count() >= 2,
            "2 OrderConfirmed consumed");
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
        await Eventually.True(() => service.Harness.GetConsumerHarness<OrderConfirmedConsumer>().Consumed.Select<OrderConfirmed>(m => m.Context.Message.OrderId == _orderId).Any(), "OrderConfirmed (Consumed)");

        await service.Harness.Bus.Publish(_placed!);
        await Eventually.True(() => service.Harness.GetConsumerHarness<OrderPlacedConsumer>().Consumed.Select<OrderPlaced>(m => m.Context.Message.OrderId == _orderId).Any(), "OrderPlaced (Consumed)");
    }

    [Then("the inbox of {string} lists, in order:")]
    public async Task ThenTheInboxListsInOrder(string customerId, DataTable table)
    {
        var inbox = await service.ClientFor(customerId).GetFromJsonAsync<List<InboxMessage>>($"/inbox/{customerId}");

        inbox!.Select(m => m.Text).ShouldBe(table.Rows.Select(r => r["message"]));
    }

    [Then("the inbox of {string} contains {string}")]
    public async Task ThenTheInboxContains(string customerId, string text)
    {
        var inbox = await service.ClientFor(customerId).GetFromJsonAsync<List<InboxMessage>>($"/inbox/{customerId}");

        inbox!.ShouldContain(m => m.Text == text);
    }
}
