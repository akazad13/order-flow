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

    [Then("StockRejected is published for that order with shortages:")]
    public async Task ThenStockRejectedIsPublished(DataTable table)
    {
        (await service.Harness.Published.Any<StockRejected>(m => m.Context.Message.OrderId == _orderId)).ShouldBeTrue();
        var rejected = service.Harness.Published.Select<StockRejected>(m => m.Context.Message.OrderId == _orderId).Single().Context.Message;

        var expected = table.Rows.Select(r => new StockShortage(r["sku"], Int(r["requested"]), Int(r["available"]))).ToList();
        rejected.Shortages.ShouldBe(expected, ignoreOrder: true);
    }

    [Then("no StockReserved is published")]
    public async Task ThenNoStockReservedIsPublished()
    {
        (await service.Harness.Published.Any<StockReserved>()).ShouldBeFalse();
    }

    [Given("an order holds {int} of {string}")]
    public async Task GivenAnOrderHolds(int quantity, string sku)
    {
        await service.Harness.Bus.Publish(new OrderPlaced(_orderId, "customer-1", [new OrderPlacedLine(sku, quantity)], DateTimeOffset.UtcNow));
        (await service.Harness.Published.Any<StockReserved>(m => m.Context.Message.OrderId == _orderId)).ShouldBeTrue();
    }

    [When("the order is cancelled")]
    public async Task WhenTheOrderIsCancelled() => await Cancel(_orderId, times: 1);

    [When("the order is cancelled twice")]
    public async Task WhenTheOrderIsCancelledTwice() => await Cancel(_orderId, times: 2);

    [When("an order that held nothing is cancelled")]
    public async Task WhenAnOrderThatHeldNothingIsCancelled()
    {
        _ = service.CreateClient();
        await Cancel(_orderId, times: 1);
    }

    [Then("StockReleased is published for that order with {int} of {string}")]
    public async Task ThenStockReleasedIsPublished(int quantity, string sku)
    {
        (await service.Harness.Published.Any<StockReleased>(m => m.Context.Message.OrderId == _orderId)).ShouldBeTrue();
        var released = service.Harness.Published.Select<StockReleased>(m => m.Context.Message.OrderId == _orderId).Single().Context.Message;

        released.Lines.ShouldBe([new StockReleasedLine(sku, quantity)]);
    }

    [Then("StockReleased is published once for that order")]
    public void ThenStockReleasedIsPublishedOnce()
    {
        service.Harness.Published.Select<StockReleased>(m => m.Context.Message.OrderId == _orderId).Count().ShouldBe(1);
    }

    [Then("no StockReleased is published")]
    public async Task ThenNoStockReleasedIsPublished()
    {
        (await service.Harness.Published.Any<StockReleased>()).ShouldBeFalse();
    }

    private async Task Cancel(Guid orderId, int times)
    {
        for (var i = 0; i < times; i++)
        {
            await service.Harness.Bus.Publish(new OrderCancelled(orderId, "customer-1", CancellationReasons.PaymentDeclined, "Card declined", DateTimeOffset.UtcNow));
        }

        var consumer = service.Harness.GetConsumerHarness<OrderCancelledConsumer>();
        var consumed = await consumer.Consumed.SelectAsync<OrderCancelled>(m => m.Context.Message.OrderId == orderId).Take(times).CountAsync();
        consumed.ShouldBe(times);
    }

    private static int Int(string value) => int.Parse(value, CultureInfo.InvariantCulture);

    private static decimal Money(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
