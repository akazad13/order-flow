using System.Net.Http.Json;
using OrderFlow.Contracts.Events;
using OrderFlow.Notification.ComponentTests.Support;
using OrderFlow.Notification.Consumers;
using OrderFlow.Notification.Inbox;
using Reqnroll;
using Shouldly;

namespace OrderFlow.Notification.ComponentTests.Steps;

[Binding]
public sealed class OrderReceivedSteps(NotificationServiceFactory service)
{
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

    [Then("the inbox of {string} contains {string}")]
    public async Task ThenTheInboxContains(string customerId, string text)
    {
        var inbox = await service.CreateClient().GetFromJsonAsync<List<InboxMessage>>($"/inbox/{customerId}");

        inbox!.ShouldContain(m => m.Text == text);
    }
}
