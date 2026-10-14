using System.Globalization;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Inventory.Api;
using OrderFlow.Inventory.ComponentTests.Support;
using OrderFlow.Inventory.Domain;
using OrderFlow.Testing;
using Reqnroll;
using Shouldly;

namespace OrderFlow.Inventory.ComponentTests.Steps;

/// <summary>Spec 017: only admins set stock; anyone may look at the catalog.</summary>
[Binding]
public sealed class ManageStockSteps(InventoryServiceFactory service)
{
    private HttpResponseMessage? _response;

    [When(@"^admin ""([^""]*)"" sets ""([^""]+)"" to name ""([^""]*)"", unit price (-?\d+\.\d{2}) and (-?\d+) available$")]
    public async Task WhenAnAdminSets(string userId, string sku, string name, string unitPrice, int available) =>
        _response = await service.AdminClient(userId).PutAsJsonAsync($"/stock/{sku}", new SetStockRequest(name, Money(unitPrice), available));

    [When(@"^(.+) tries to set ""([^""]+)"" to (\d+) available$")]
    public async Task WhenSomeoneTriesToSet(string who, string sku, int available)
    {
        var client = service.CreateClient();
        var token = Who(who);
        if (token is not null)
        {
            client.WithToken(token);
        }

        _response = await client.PutAsJsonAsync($"/stock/{sku}", new SetStockRequest("Changed", 1.00m, available));
    }

    [Then("the answer is {int}")]
    public void ThenTheAnswerIs(int status) => ((int)_response!.StatusCode).ShouldBe(status);

    [Then("the answer is 400 with an error for each of {string}, {string} and {string}")]
    public async Task ThenTheAnswerIs400WithErrors(string first, string second, string third)
    {
        ((int)_response!.StatusCode).ShouldBe(400);
        var problem = await _response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Keys.ShouldBe([first, second, third], ignoreOrder: true);
    }

    [Then(@"^anyone looking at the catalog sees ""([^""]+)"" as ""([^""]+)"" at (\d+\.\d{2}) with (\d+) available$")]
    public async Task ThenAnyoneSees(string sku, string name, string unitPrice, int available)
    {
        // No login: the catalog is open to everyone (AC-3).
        var item = await service.CreateClient().GetFromJsonAsync<CatalogItem>($"/stock/{sku}");
        item.ShouldBe(new CatalogItem(sku, name, Money(unitPrice), available));
    }

    /// <summary>A token for the caller as the scenario names them; null for no login.</summary>
    private static string? Who(string who) => who switch
    {
        "someone who is not logged in" => null,
        _ when who.StartsWith("customer ", StringComparison.Ordinal) => TestTokens.For(Quoted(who)),
        _ when who.StartsWith("staff member ", StringComparison.Ordinal) => TestTokens.Staff(Quoted(who)),
        _ when who.StartsWith("a login with the role ", StringComparison.Ordinal) => TestTokens.For("someone", Quoted(who)),
        _ => throw new ArgumentOutOfRangeException(nameof(who), who, "Unknown caller."),
    };

    private static string Quoted(string text) => text.Split('"')[1];

    private static decimal Money(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
