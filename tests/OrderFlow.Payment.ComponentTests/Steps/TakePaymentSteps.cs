using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Contracts.Events;
using OrderFlow.Payment.ComponentTests.Support;
using OrderFlow.Payment.Consumers;
using OrderFlow.Payment.Gateway;
using Reqnroll;
using Shouldly;

namespace OrderFlow.Payment.ComponentTests.Steps;

[Binding]
public sealed class TakePaymentSteps(PaymentServiceFactory service)
{
    private readonly Guid _orderId = Guid.NewGuid();

    [Given("payments for customer {string} are declined with {string}")]
    public async Task GivenPaymentsAreDeclined(string customerId, string reason)
    {
        var response = await service.CreateClient().PutAsJsonAsync($"/test/declines/{customerId}", new DeclineRule(reason));
        response.EnsureSuccessStatusCode();
    }

    [When(@"^stock is reserved for an order with a total of (\d+\.\d{2})$")]
    public Task WhenStockIsReserved(string total) => ReserveStock("customer-1", total);

    [When(@"^stock is reserved for an order by customer ""([^""]+)"" with a total of (\d+\.\d{2})$")]
    public Task WhenStockIsReservedForCustomer(string customerId, string total) => ReserveStock(customerId, total);

    [Then("PaymentFailed is published for that order with reason {string}")]
    public async Task ThenPaymentFailedIsPublished(string reason)
    {
        (await service.Harness.Published.Any<PaymentFailed>(m => m.Context.Message.OrderId == _orderId)).ShouldBeTrue();
        var failed = service.Harness.Published.Select<PaymentFailed>(m => m.Context.Message.OrderId == _orderId).Single().Context.Message;

        failed.Reason.ShouldBe(reason);
    }

    [Then("the customer is not charged for that order")]
    public void ThenTheCustomerIsNotCharged()
    {
        service.Services.GetRequiredService<FakePaymentGateway>().ChargesFor(_orderId).ShouldBeEmpty();
    }

    [Then("no PaymentSucceeded is published")]
    public async Task ThenNoPaymentSucceededIsPublished()
    {
        (await service.Harness.Published.Any<PaymentSucceeded>()).ShouldBeFalse();
    }

    private async Task ReserveStock(string customerId, string total)
    {
        _ = service.CreateClient();
        var reserved = new StockReserved(
            _orderId,
            customerId,
            [new StockReservedLine("TEST-A", 1, Money(total))],
            Money(total),
            DateTimeOffset.UtcNow);

        await service.Harness.Bus.Publish(reserved);

        var consumer = service.Harness.GetConsumerHarness<StockReservedConsumer>();
        (await consumer.Consumed.Any<StockReserved>(m => m.Context.Message.OrderId == _orderId)).ShouldBeTrue();
    }

    [Then(@"^the customer is charged (\d+\.\d{2}) exactly once for that order$")]
    public void ThenTheCustomerIsChargedOnce(string amount)
    {
        var gateway = service.Services.GetRequiredService<FakePaymentGateway>();

        gateway.ChargesFor(_orderId).ShouldHaveSingleItem().Amount.ShouldBe(Money(amount));
    }

    [Then(@"^PaymentSucceeded is published for that order with an amount of (\d+\.\d{2})$")]
    public async Task ThenPaymentSucceededIsPublished(string amount)
    {
        (await service.Harness.Published.Any<PaymentSucceeded>(m => m.Context.Message.OrderId == _orderId)).ShouldBeTrue();
        var paid = service.Harness.Published.Select<PaymentSucceeded>(m => m.Context.Message.OrderId == _orderId).Single().Context.Message;

        paid.Amount.ShouldBe(Money(amount));
    }

    private static decimal Money(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
