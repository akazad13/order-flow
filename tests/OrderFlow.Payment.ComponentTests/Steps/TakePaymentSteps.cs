using System.Globalization;
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

    [When(@"^stock is reserved for an order with a total of (\d+\.\d{2})$")]
    public async Task WhenStockIsReserved(string total)
    {
        _ = service.CreateClient();
        var reserved = new StockReserved(
            _orderId,
            "customer-1",
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
