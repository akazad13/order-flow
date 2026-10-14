using System.Net;
using System.Net.Http.Json;
using OrderFlow.OrderService.Api;
using OrderFlow.OrderService.ComponentTests.Support;
using OrderFlow.Testing;
using Reqnroll;
using Shouldly;

namespace OrderFlow.OrderService.ComponentTests.Steps;

/// <summary>Spec 012: who may place, read and cancel orders.</summary>
[Binding]
public sealed class OrderAccessSteps(OrderServiceFactory service, OrderLifecycleSteps lifecycle)
{
    private static readonly PlaceOrderRequest AnOrder = new([new OrderLineDto("SKU-001", 1)]);

    private HttpResponseMessage? _response;
    private string? _caller;

    [When(@"^someone who is not logged in tries to (place an order|read an order|cancel an order)$")]
    public async Task WhenSomeoneNotLoggedInTries(string action)
    {
        var client = service.CreateClient();
        _response = action switch
        {
            "place an order" => await client.PostAsJsonAsync("/orders", AnOrder),
            "read an order" => await client.GetAsync($"/orders/{Guid.NewGuid()}"),
            _ => await client.PostAsync($"/orders/{Guid.NewGuid()}/cancellation", null),
        };
    }

    [When("customer {string} reads that order")]
    public async Task WhenCustomerReadsThatOrder(string customerId)
    {
        _caller = customerId;
        _response = await service.ClientFor(customerId).GetAsync($"/orders/{lifecycle.Placed.OrderId}");
    }

    [When("staff member {string} reads that order")]
    public async Task WhenStaffReadsThatOrder(string userId) =>
        _response = await service.StaffClient(userId).GetAsync($"/orders/{lifecycle.Placed.OrderId}");

    [When("customer {string} with the role {string} reads that order")]
    public async Task WhenCustomerWithARoleReadsThatOrder(string customerId, string role)
    {
        _caller = customerId;
        _response = await service.CreateClient().WithToken(TestTokens.For(customerId, role)).GetAsync($"/orders/{lifecycle.Placed.OrderId}");
    }

    [Then("the answer is that order, placed by customer {string}")]
    public async Task ThenTheAnswerIsThatOrder(string customerId)
    {
        // Spec 015, R-1: staff see the order as it is; it stays the customer's.
        _response!.StatusCode.ShouldBe(HttpStatusCode.OK);
        var order = await _response.Content.ReadFromJsonAsync<OrderResponse>();
        order!.OrderId.ShouldBe(lifecycle.Placed.OrderId);
        order.CustomerId.ShouldBe(customerId);
    }

    [When("customer {string} asks to cancel that order")]
    public async Task WhenCustomerAsksToCancelThatOrder(string customerId) =>
        _response = await service.ClientFor(customerId).PostAsync($"/orders/{lifecycle.Placed.OrderId}/cancellation", null);

    [When(@"^customer ""([^""]+)"" places an order with a token that is (.+)$")]
    public async Task WhenCustomerPlacesWithABadToken(string customerId, string problem) =>
        _response = await service.CreateClient().WithToken(TestTokens.Bad(customerId, problem)).PostAsJsonAsync("/orders", AnOrder);

    [Then("the answer is {int}")]
    public void ThenTheAnswerIs(int status) => ((int)_response!.StatusCode).ShouldBe(status);

    [Then("the answer is 404, exactly as for an order that does not exist")]
    public async Task ThenTheAnswerIsLikeAnUnknownOrder()
    {
        // Spec 012, R-2: nothing in the answer tells the caller the order exists.
        var unknown = await service.ClientFor(_caller!).GetAsync($"/orders/{Guid.NewGuid()}");

        _response!.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await _response.Content.ReadAsStringAsync()).ShouldBe(await unknown.Content.ReadAsStringAsync());
    }
}
