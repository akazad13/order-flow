using System.Globalization;
using MassTransit.Testing;
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

/// <summary>Spec 006: Payment hears that "that order" was cancelled.</summary>
[Binding]
public sealed class SettlePaymentSteps(PaymentServiceFactory service, TakePaymentSteps payment)
{
    private int _cancellationsSent;

    [Given("payments for customer {string} are stalled")]
    public async Task GivenPaymentsAreStalled(string customerId)
    {
        var response = await service.CreateClient().PutAsync($"/test/stalls/{customerId}", null);
        response.EnsureSuccessStatusCode();
    }

    [When("that order is cancelled at the customer's request")]
    public Task WhenThatOrderIsCancelledByTheCustomer() => Cancel(CancellationReasons.CustomerRequested, times: 1);

    [When("that order is cancelled at the customer's request twice")]
    public Task WhenThatOrderIsCancelledByTheCustomerTwice() => Cancel(CancellationReasons.CustomerRequested, times: 2);

    [When("that order is cancelled because the payment was declined")]
    public Task WhenThatOrderIsCancelledForADecline() => Cancel(CancellationReasons.PaymentDeclined, times: 1);

    [Then(@"^PaymentRefunded is published for that order with an amount of (\d+\.\d{2})$")]
    public async Task ThenPaymentRefundedIsPublished(string amount)
    {
        await Eventually.True(() => Refunds().Any(), "PaymentRefunded (Published)");
        var refunded = Refunds().Single();

        refunded.Amount.ShouldBe(Money(amount));
        refunded.CustomerId.ShouldBe("customer-1");
    }

    [Then("PaymentRefunded is published once for that order")]
    public void ThenPaymentRefundedIsPublishedOnce() => Refunds().Count().ShouldBe(1);

    [Then(@"^the customer is refunded (\d+\.\d{2}) exactly once for that order$")]
    public void ThenTheCustomerIsRefundedOnce(string amount) =>
        service.Services.GetRequiredService<InMemoryPaymentLedger>().RefundsFor(payment.OrderId).ShouldHaveSingleItem().Amount.ShouldBe(Money(amount));

    [Then("no PaymentRefunded is published")]
    public async Task ThenNoPaymentRefundedIsPublished() =>
        (await service.Harness.Published.Any<PaymentRefunded>()).ShouldBeFalse();

    [Then("no PaymentFailed is published")]
    public async Task ThenNoPaymentFailedIsPublished() =>
        (await service.Harness.Published.Any<PaymentFailed>()).ShouldBeFalse();

    private IEnumerable<PaymentRefunded> Refunds() =>
        service.Harness.Published.Select<PaymentRefunded>(m => m.Context.Message.OrderId == payment.OrderId).Select(m => m.Context.Message);

    private async Task Cancel(string reason, int times)
    {
        _ = service.CreateClient();
        for (var i = 0; i < times; i++)
        {
            // Real repeats: same content, new message IDs.
            await service.Harness.Bus.Publish(new OrderCancelledV2(payment.OrderId, "customer-1", reason, "detail", DateTimeOffset.UtcNow));
        }

        _cancellationsSent += times;
        var consumer = service.Harness.GetConsumerHarness<OrderCancelledConsumer>();
        await Eventually.True(
            () => consumer.Consumed.Select<OrderCancelledV2>(m => m.Context.Message.OrderId == payment.OrderId).Count() >= _cancellationsSent,
            $"{_cancellationsSent} OrderCancelled consumed");
    }

    private static decimal Money(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
