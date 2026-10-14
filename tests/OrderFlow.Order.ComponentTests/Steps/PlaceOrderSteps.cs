using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Contracts.Events;
using OrderFlow.OrderService.Api;
using OrderFlow.OrderService.ComponentTests.Support;
using OrderFlow.Testing;
using Reqnroll;
using Shouldly;

namespace OrderFlow.OrderService.ComponentTests.Steps;

[Binding]
public sealed class PlaceOrderSteps(OrderServiceFactory service)
{
    private HttpResponseMessage? _response;
    private OrderResponse? _order;

    [When("customer {string} places an order for {int} x {string}")]
    public async Task WhenCustomerPlacesAnOrder(string customerId, int quantity, string sku)
    {
        var client = service.CreateClient();
        _response = await client.PostAsJsonAsync("/orders", new PlaceOrderRequest(customerId, [new OrderLineDto(sku, quantity)]));
    }

    [Then("the order is accepted with status {string}")]
    public async Task ThenTheOrderIsAccepted(string status)
    {
        _response!.StatusCode.ShouldBe(HttpStatusCode.Created);
        _order = await _response.Content.ReadFromJsonAsync<OrderResponse>();
        _order!.Status.ShouldBe(status);
        _response.Headers.Location!.ToString().ShouldBe($"/orders/{_order.OrderId}");
    }

    [Then("an OrderPlaced event is published for that order")]
    public Task ThenAnOrderPlacedEventIsPublished() =>
        Eventually.True(() => service.Harness.Published.Select<OrderPlaced>(m => m.Context.Message.OrderId == _order!.OrderId).Any(), "OrderPlaced (Published)");

    [Then("the order is rejected with an error for {string}")]
    public async Task ThenTheOrderIsRejected(string field)
    {
        _response!.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await _response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Keys.ShouldContain(field);
    }

    [Then("no OrderPlaced event is published")]
    public async Task ThenNoOrderPlacedEventIsPublished()
    {
        var published = await service.Harness.Published.Any<OrderPlaced>();
        published.ShouldBeFalse();
    }
}
