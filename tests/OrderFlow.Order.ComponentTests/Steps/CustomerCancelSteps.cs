using System.Net;
using System.Net.Http.Json;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Contracts.Events;
using OrderFlow.OrderService.Api;
using OrderFlow.OrderService.ComponentTests.Support;
using OrderFlow.OrderService.StateMachine;
using Reqnroll;
using Shouldly;

namespace OrderFlow.OrderService.ComponentTests.Steps;

/// <summary>Spec 006: the customer asks the Order API to cancel "that order".</summary>
[Binding]
public sealed class CustomerCancelSteps(OrderServiceFactory service, OrderLifecycleSteps lifecycle)
{
    private HttpResponseMessage? _response;

    private ISagaStateMachineTestHarness<OrderStateMachine, OrderState> Saga =>
        service.Harness.GetSagaStateMachineHarness<OrderStateMachine, OrderState>();

    [When("the customer asks to cancel that order")]
    public async Task WhenTheCustomerAsksToCancel() =>
        _response = await service.ClientFor(lifecycle.Placed.CustomerId).PostAsync($"/orders/{lifecycle.Placed.OrderId}/cancellation", null);

    [When("staff member {string} asks to cancel that order")]
    public async Task WhenStaffAsksToCancel(string userId) =>
        _response = await service.StaffClient(userId).PostAsync($"/orders/{lifecycle.Placed.OrderId}/cancellation", null);

    [When("the customer has cancelled that order")]
    public async Task WhenTheCustomerHasCancelled()
    {
        await WhenTheCustomerAsksToCancel();
        _response!.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        (await Saga.Exists(lifecycle.Placed.OrderId, machine => machine.Cancelled)).ShouldNotBeNull("the order was never cancelled");
    }

    [When("the customer asks to cancel an order that does not exist")]
    public async Task WhenTheCustomerAsksToCancelAnUnknownOrder() =>
        _response = await service.ClientFor("customer-1").PostAsync($"/orders/{Guid.NewGuid()}/cancellation", null);

    [When("the order placement is processed")]
    public async Task WhenTheOrderPlacementIsProcessed()
    {
        var order = lifecycle.Placed;
        await service.Harness.Bus.Publish(new OrderPlaced(order.OrderId, order.CustomerId, order.Lines.Select(l => new OrderPlacedLine(l.Sku, l.Quantity)).ToList(), order.PlacedAt));
    }

    [Then("the request is accepted")]
    public async Task ThenTheRequestIsAccepted()
    {
        _response!.StatusCode.ShouldBe(HttpStatusCode.Accepted);
        var order = await _response.Content.ReadFromJsonAsync<OrderResponse>();
        order!.OrderId.ShouldBe(lifecycle.Placed.OrderId);
    }

    [Then("the request is refused as too late")]
    public async Task ThenTheRequestIsRefusedAsTooLate()
    {
        _response!.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var problem = await _response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Title.ShouldBe("Too late to cancel.");
    }

    [When("{int} hours pass")]
    public void WhenHoursPass(int hours) => service.Clock.Advance(TimeSpan.FromHours(hours));

    [When("a cancellation asked for {int} hours after confirmation is handled {int} hours after it")]
    public async Task WhenALateHandledCancellationArrives(int askedAfter, int handledAfter)
    {
        // Spec 022, AC-3: the request's own time decides, not when it is handled.
        var confirmedAt = service.Clock.GetUtcNow();
        service.Clock.Advance(TimeSpan.FromHours(handledAfter));
        await service.Harness.Bus.Publish(new OrderCancellationRequested(lifecycle.Placed.OrderId, confirmedAt.AddHours(askedAfter), CancellationRequesters.Customer));
    }

    [Then("the order can be cancelled until {int} hours after it was confirmed")]
    public async Task ThenTheOrderCanBeCancelledUntil(int hours)
    {
        // The clock has not moved since the payment, so "now" is the confirmation time.
        var order = await service.ClientFor(lifecycle.Placed.CustomerId).GetFromJsonAsync<OrderResponse>($"/orders/{lifecycle.Placed.OrderId}");
        order!.CancellableUntil.ShouldBe(service.Clock.GetUtcNow().AddHours(hours));
    }

    [Then("the order is returned as already cancelled")]
    public async Task ThenTheOrderIsReturnedAsAlreadyCancelled()
    {
        _response!.StatusCode.ShouldBe(HttpStatusCode.OK);
        var order = await _response.Content.ReadFromJsonAsync<OrderResponse>();
        order!.Status.ShouldBe("Cancelled");
    }

    [Then("the request is refused as not found")]
    public void ThenTheRequestIsRefusedAsNotFound() => _response!.StatusCode.ShouldBe(HttpStatusCode.NotFound);

    [Then("no cancellation request is published")]
    public async Task ThenNoCancellationRequestIsPublished() =>
        (await service.Harness.Published.Any<OrderCancellationRequested>()).ShouldBeFalse();
}
