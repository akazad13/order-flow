using System.Net.Http.Json;
using OrderFlow.Contracts.Events;
using OrderFlow.IntegrationTesting;
using OrderFlow.Inventory.Api;
using OrderFlow.Inventory.Domain;

namespace OrderFlow.Inventory.IntegrationTests;

/// <summary>
/// Spec 007 against real PostgreSQL and RabbitMQ. The Inventory service runs in-process;
/// the test bus plays the Order service.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class InventoryServiceTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Holds_survive_a_restart_and_are_released_after_it()
    {
        // AC-2
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        var orderId = Guid.NewGuid();

        await using (var before = new ServiceHost<Program>(settings))
        {
            var client = await before.StartAsync();
            await SetStock(client, "ITEM-1", 10);

            await testBus.Publish(new OrderPlaced(orderId, "customer-1", [new OrderPlacedLine("ITEM-1", 3)], DateTimeOffset.UtcNow));
            await testBus.WaitFor<StockReserved>(m => m.OrderId == orderId);
            (await Available(client, "ITEM-1")).ShouldBe(7);
        }

        await using var after = new ServiceHost<Program>(settings);
        var restarted = await after.StartAsync();
        (await Available(restarted, "ITEM-1")).ShouldBe(7);

        await testBus.Publish(new OrderCancelled(orderId, "customer-1", CancellationReasons.PaymentDeclined, "Card declined", DateTimeOffset.UtcNow));
        var released = await testBus.WaitFor<StockReleased>(m => m.OrderId == orderId);

        released.Lines.ShouldBe([new StockReleasedLine("ITEM-1", 3)]);
        (await Available(restarted, "ITEM-1")).ShouldBe(10);
    }

    [Fact]
    public async Task Twenty_orders_for_ten_items_hold_exactly_ten()
    {
        // AC-4
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        await using var host = new ServiceHost<Program>(settings);
        var client = await host.StartAsync();
        await SetStock(client, "ITEM-2", 10);

        var ids = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToHashSet();
        await Task.WhenAll(ids.Select(id =>
            testBus.Publish(new OrderPlaced(id, "customer-1", [new OrderPlacedLine("ITEM-2", 1)], DateTimeOffset.UtcNow))));

        await WaitForOutcomes(testBus, ids, TimeSpan.FromSeconds(60));

        testBus.Received<StockReserved>().Count(m => ids.Contains(m.OrderId)).ShouldBe(10);
        testBus.Received<StockRejected>().Count(m => ids.Contains(m.OrderId)).ShouldBe(10);
        (await Available(client, "ITEM-2")).ShouldBe(0);
    }

    private async Task<Dictionary<string, string?>> SettingsAsync()
    {
        var settings = infrastructure.RabbitSettings(await infrastructure.CreateVirtualHostAsync());
        settings["Persistence:Provider"] = "Postgres";
        settings["ConnectionStrings:Inventory"] = infrastructure.ConnectionString($"inventory_{Guid.NewGuid():N}");
        settings[StockEndpoints.EnableTestEndpointsKey] = "true";
        return settings;
    }

    private static async Task SetStock(HttpClient client, string sku, int available)
    {
        var response = await client.PutAsJsonAsync($"/stock/{sku}", new SetStockRequest($"Item {sku}", 5.00m, available));
        response.EnsureSuccessStatusCode();
    }

    private static async Task<int> Available(HttpClient client, string sku) =>
        (await client.GetFromJsonAsync<CatalogItem>($"/stock/{sku}"))!.Available;

    private static async Task WaitForOutcomes(TestBus testBus, IReadOnlySet<Guid> ids, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (testBus.Received<StockReserved>().Count(m => ids.Contains(m.OrderId)) +
               testBus.Received<StockRejected>().Count(m => ids.Contains(m.OrderId)) < ids.Count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException("Not every order got a StockReserved or StockRejected in time.");
            }

            await Task.Delay(50);
        }
    }
}
