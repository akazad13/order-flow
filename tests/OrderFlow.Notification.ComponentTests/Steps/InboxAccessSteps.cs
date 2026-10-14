using OrderFlow.Notification.ComponentTests.Support;
using Reqnroll;
using Shouldly;

namespace OrderFlow.Notification.ComponentTests.Steps;

/// <summary>Spec 012: who may read an inbox.</summary>
[Binding]
public sealed class InboxAccessSteps(NotificationServiceFactory service)
{
    private HttpResponseMessage? _response;

    [When("someone who is not logged in reads the inbox of {string}")]
    public async Task WhenSomeoneNotLoggedInReads(string customerId) =>
        _response = await service.CreateClient().GetAsync($"/inbox/{customerId}");

    [When("customer {string} reads the inbox of {string}")]
    public async Task WhenCustomerReadsTheInboxOf(string caller, string customerId) =>
        _response = await service.ClientFor(caller).GetAsync($"/inbox/{customerId}");

    [Then("the answer is {int}")]
    public void ThenTheAnswerIs(int status) => ((int)_response!.StatusCode).ShouldBe(status);
}
