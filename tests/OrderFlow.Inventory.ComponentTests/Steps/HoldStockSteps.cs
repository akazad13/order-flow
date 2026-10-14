using System.Globalization;
using System.Net.Http.Json;
using OrderFlow.Contracts.Events;
using OrderFlow.Inventory.Api;
using OrderFlow.Inventory.ComponentTests.Support;
using OrderFlow.Inventory.Consumers;
using OrderFlow.Inventory.Domain;
using OrderFlow.Testing;
using Reqnroll;
using Shouldly;

namespace OrderFlow.Inventory.ComponentTests.Steps;

[Binding]
public sealed class HoldStockSteps(InventoryServiceFactory service)
{
    private readonly Guid _orderId = Guid.NewGuid();
    private StockReserved? _reserved;
    private OrderPlaced? _lastPlaced;
    private int _placementsSent;

    [When("an order for {int} of {string} is placed twice")]
    public async Task WhenAnOrderIsPlacedTwice(int quantity, string sku)
    {
        _lastPlaced = new OrderPlaced(_orderId, "customer-1", [new OrderPlacedLine(sku, quantity)], DateTimeOffset.UtcNow);
        await SendPlacement(_lastPlaced);
        await SendPlacement(_lastPlaced);
    }

    [When("an order for {int} of {string} is placed")]
    public async Task WhenAnOrderIsPlaced(int quantity, string sku)
    {
        _lastPlaced = new OrderPlaced(_orderId, "customer-1", [new OrderPlacedLine(sku, quantity)], DateTimeOffset.UtcNow);
        await SendPlacement(_lastPlaced);
    }

    [When("the stock of {string} is refilled to {int}")]
    public async Task WhenTheStockIsRefilled(string sku, int available)
    {
        var response = await service.CreateClient().PutAsJsonAsync($"/stock/{sku}", new SetStockRequest("Refilled item", 6.25m, available));
        response.EnsureSuccessStatusCode();
    }

    [When("the same order placement arrives again")]
    public async Task WhenTheSameOrderPlacementArrivesAgain()
    {
        // A real repeat: same content, new message ID, so no inbox can hide it.
        await SendPlacement(_lastPlaced!);
    }

    [Then("StockReserved is published once for that order")]
    public void ThenStockReservedIsPublishedOnce() =>
        service.Harness.Published.Select<StockReserved>(m => m.Context.Message.OrderId == _orderId).Count().ShouldBe(1);

    [Then("StockRejected is published once for that order")]
    public void ThenStockRejectedIsPublishedOnce() =>
        service.Harness.Published.Select<StockRejected>(m => m.Context.Message.OrderId == _orderId).Count().ShouldBe(1);

    /// <summary>Publishes a placement and waits until the consumer has handled it.</summary>
    private async Task SendPlacement(OrderPlaced placement)
    {
        _ = service.CreateClient();
        await service.Harness.Bus.Publish(placement);
        _placementsSent++;

        var consumer = service.Harness.GetConsumerHarness<OrderPlacedConsumer>();
        await Eventually.True(
            () => consumer.Consumed.Select<OrderPlaced>(m => m.Context.Message.OrderId == _orderId).Count() >= _placementsSent,
            $"{_placementsSent} OrderPlaced consumed");
    }

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
        await Eventually.True(() => consumer.Consumed.Select<OrderPlaced>(m => m.Context.Message.OrderId == _orderId).Any(), "OrderPlaced (Consumed)");
    }

    [Then(@"^StockReserved is published for that order with a total of (\d+\.\d{2})$")]
    public async Task ThenStockReservedIsPublished(string total)
    {
        await Eventually.True(() => service.Harness.Published.Select<StockReserved>(m => m.Context.Message.OrderId == _orderId).Any(), "StockReserved (Published)");
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
        await Eventually.True(() => service.Harness.Published.Select<StockRejected>(m => m.Context.Message.OrderId == _orderId).Any(), "StockRejected (Published)");
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
        _lastPlaced = new OrderPlaced(_orderId, "customer-1", [new OrderPlacedLine(sku, quantity)], DateTimeOffset.UtcNow);
        await service.Harness.Bus.Publish(_lastPlaced);
        _placementsSent++;
        await Eventually.True(() => service.Harness.Published.Select<StockReserved>(m => m.Context.Message.OrderId == _orderId).Any(), "StockReserved (Published)");
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
        await Eventually.True(() => service.Harness.Published.Select<StockReleased>(m => m.Context.Message.OrderId == _orderId).Any(), "StockReleased (Published)");
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
        await Eventually.True(
            () => consumer.Consumed.Select<OrderCancelled>(m => m.Context.Message.OrderId == orderId).Count() >= times,
            $"{times} OrderCancelled consumed");
    }

    private static int Int(string value) => int.Parse(value, CultureInfo.InvariantCulture);

    private static decimal Money(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
