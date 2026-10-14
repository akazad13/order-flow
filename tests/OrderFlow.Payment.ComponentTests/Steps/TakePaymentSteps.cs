using System.Globalization;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Contracts.Events;
using OrderFlow.Payment.ComponentTests.Support;
using OrderFlow.Payment.Consumers;
using OrderFlow.Payment.Gateway;
using OrderFlow.Payment.Ledger;
using OrderFlow.Testing;
using Reqnroll;
using Shouldly;

namespace OrderFlow.Payment.ComponentTests.Steps;

[Binding]
public sealed class TakePaymentSteps(PaymentServiceFactory service)
{
    private readonly Guid _orderId = Guid.NewGuid();

    /// <summary>The scenario's order ("that order"); shared with <see cref="SettlePaymentSteps"/>.</summary>
    internal Guid OrderId => _orderId;

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

    [When("stock is rejected for an order")]
    public async Task WhenStockIsRejected()
    {
        // Spec 002, AC-3: Payment does not listen for shortages, so nothing may happen.
        _ = service.CreateClient();
        await service.Harness.Bus.Publish(new StockRejected(_orderId, "customer-1", [new StockShortage("TEST-A", 2, 1)], DateTimeOffset.UtcNow));
    }

    [Then("PaymentFailed is published for that order with reason {string}")]
    public async Task ThenPaymentFailedIsPublished(string reason)
    {
        await Eventually.True(() => service.Harness.Published.Select<PaymentFailed>(m => m.Context.Message.OrderId == _orderId).Any(), "PaymentFailed (Published)");
        var failed = service.Harness.Published.Select<PaymentFailed>(m => m.Context.Message.OrderId == _orderId).Single().Context.Message;

        failed.Reason.ShouldBe(reason);
    }

    [Then("the customer is not charged for that order")]
    public void ThenTheCustomerIsNotCharged()
    {
        service.Services.GetRequiredService<InMemoryPaymentLedger>().ChargesFor(_orderId).ShouldBeEmpty();
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
        await Eventually.True(() => consumer.Consumed.Select<StockReserved>(m => m.Context.Message.OrderId == _orderId).Any(), "StockReserved (Consumed)");
    }

    [Then(@"^the customer is charged (\d+\.\d{2}) exactly once for that order$")]
    public void ThenTheCustomerIsChargedOnce(string amount)
    {
        var gateway = service.Services.GetRequiredService<InMemoryPaymentLedger>();

        gateway.ChargesFor(_orderId).ShouldHaveSingleItem().Amount.ShouldBe(Money(amount));
    }

    [Then(@"^PaymentSucceeded is published for that order with an amount of (\d+\.\d{2})$")]
    public async Task ThenPaymentSucceededIsPublished(string amount)
    {
        await Eventually.True(() => service.Harness.Published.Select<PaymentSucceeded>(m => m.Context.Message.OrderId == _orderId).Any(), "PaymentSucceeded (Published)");
        var paid = service.Harness.Published.Select<PaymentSucceeded>(m => m.Context.Message.OrderId == _orderId).Single().Context.Message;

        paid.Amount.ShouldBe(Money(amount));
    }

    [When(@"^stock is reserved twice for an order by customer ""([^""]+)"" with a total of (\d+\.\d{2})$")]
    public async Task WhenStockIsReservedTwice(string customerId, string total)
    {
        _ = service.CreateClient();
        var reserved = new StockReserved(_orderId, customerId, [new StockReservedLine("TEST-A", 1, Money(total))], Money(total), DateTimeOffset.UtcNow);

        // Two real repeats: same content, new message IDs.
        await service.Harness.Bus.Publish(reserved);
        await service.Harness.Bus.Publish(reserved);

        var consumer = service.Harness.GetConsumerHarness<StockReservedConsumer>();
        await Eventually.True(
            () => consumer.Consumed.Select<StockReserved>(m => m.Context.Message.OrderId == _orderId).Count() >= 2,
            "2 StockReserved consumed");
    }

    [Then("PaymentSucceeded is published once for that order")]
    public void ThenPaymentSucceededIsPublishedOnce() =>
        service.Harness.Published.Select<PaymentSucceeded>(m => m.Context.Message.OrderId == _orderId).Count().ShouldBe(1);

    [Then("PaymentFailed is published once for that order")]
    public void ThenPaymentFailedIsPublishedOnce() =>
        service.Harness.Published.Select<PaymentFailed>(m => m.Context.Message.OrderId == _orderId).Count().ShouldBe(1);

    private static decimal Money(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
