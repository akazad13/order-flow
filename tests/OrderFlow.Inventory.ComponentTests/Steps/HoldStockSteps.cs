using System.Globalization;
using System.Net.Http.Json;
using OrderFlow.Contracts.Events;
using OrderFlow.Inventory.Api;
using OrderFlow.Inventory.ComponentTests.Support;
using OrderFlow.Inventory.Consumers;
using OrderFlow.Inventory.Domain;
using Reqnroll;
using Shouldly;

namespace OrderFlow.Inventory.ComponentTests.Steps;

[Binding]
public sealed class HoldStockSteps(InventoryServiceFactory service)
{
    private readonly Guid _orderId = Guid.NewGuid();
    private StockReserved? _reserved;

    [Given("the catalog has:")]
    public async Task GivenTheCatalogHas(DataTable table)
    {
        var client = service.CreateClient();
        foreach (var row in table.Rows)
        {
            var response = await client.PutAsJsonAsync($"/stock/{row["sku"]}", new SetStockRequest(
                row["name"],
                Money(row["unitPrice"]),
                int.Parse(row["available"], CultureInfo.InvariantCulture)));
            response.EnsureSuccessStatusCode();
        }
    }

    [When("an order is placed with:")]
    public async Task WhenAnOrderIsPlacedWith(DataTable table)
    {
        var lines = table.Rows
            .Select(r => new OrderPlacedLine(r["sku"], int.Parse(r["quantity"], CultureInfo.InvariantCulture)))
            .ToList();

        await service.Harness.Bus.Publish(new OrderPlaced(_orderId, "customer-1", lines, DateTimeOffset.UtcNow));

        var consumer = service.Harness.GetConsumerHarness<OrderPlacedConsumer>();
        (await consumer.Consumed.Any<OrderPlaced>(m => m.Context.Message.OrderId == _orderId)).ShouldBeTrue();
    }

    [Then(@"^StockReserved is published for that order with a total of (\d+\.\d{2})$")]
    public async Task ThenStockReservedIsPublished(string total)
    {
        (await service.Harness.Published.Any<StockReserved>(m => m.Context.Message.OrderId == _orderId)).ShouldBeTrue();
        _reserved = service.Harness.Published.Select<StockReserved>(m => m.Context.Message.OrderId == _orderId).Single().Context.Message;
        _reserved.Total.ShouldBe(Money(total));
    }

    [Then("the reserved lines are:")]
    public void ThenTheReservedLinesAre(DataTable table)
    {
        var expected = table.Rows
            .Select(r => new StockReservedLine(r["sku"], int.Parse(r["quantity"], CultureInfo.InvariantCulture), Money(r["unitPrice"])))
            .ToList();

        _reserved!.Lines.ShouldBe(expected, ignoreOrder: true);
    }

    [Then("the available stock is:")]
    public async Task ThenTheAvailableStockIs(DataTable table)
    {
        var client = service.CreateClient();
        foreach (var row in table.Rows)
        {
            var item = await client.GetFromJsonAsync<CatalogItem>($"/stock/{row["sku"]}");
            item!.Available.ShouldBe(int.Parse(row["available"], CultureInfo.InvariantCulture), $"available stock of {row["sku"]}");
        }
    }

    private static decimal Money(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
