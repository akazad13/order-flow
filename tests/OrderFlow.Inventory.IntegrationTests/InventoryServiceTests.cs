using System.Net.Http.Json;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Contracts.Events;
using OrderFlow.IntegrationTesting;
using OrderFlow.Inventory.Api;
using OrderFlow.Inventory.Domain;
using OrderFlow.Inventory.Persistence;
using OrderFlow.Testing;

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

    [Fact]
    public async Task Two_copies_of_one_order_at_once_hold_once()
    {
        // Spec 004, AC-6
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        await using var host = new ServiceHost<Program>(settings);
        var client = await host.StartAsync();
        await SetStock(client, "ITEM-3", 10);

        // Same content, two message IDs: the inbox cannot hide either copy.
        var orderId = Guid.NewGuid();
        var placed = new OrderPlaced(orderId, "customer-1", [new OrderPlacedLine("ITEM-3", 3)], DateTimeOffset.UtcNow);
        await Task.WhenAll(testBus.Publish(placed), testBus.Publish(placed));

        await WaitUntilConsumed(host, messages: 2);

        testBus.Received<StockReserved>().Count(m => m.OrderId == orderId).ShouldBe(1);
        (await Available(client, "ITEM-3")).ShouldBe(7);
    }

    [Fact]
    public async Task Orders_cancelled_while_being_placed_end_up_holding_nothing()
    {
        // Spec 006, AC-4: each order's cancellation and placement arrive at the same moment.
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        await using var host = new ServiceHost<Program>(settings);
        var client = await host.StartAsync();
        await SetStock(client, "ITEM-4", 10);

        var ids = Enumerable.Range(0, 10).Select(_ => Guid.NewGuid()).ToList();
        await Task.WhenAll(ids.SelectMany(id => new[]
        {
            testBus.Publish(new OrderCancelled(id, "customer-1", CancellationReasons.CustomerRequested, "Cancelled at the customer's request.", DateTimeOffset.UtcNow)),
            testBus.Publish(new OrderPlaced(id, "customer-1", [new OrderPlacedLine("ITEM-4", 1)], DateTimeOffset.UtcNow)),
        }));

        await WaitUntilConsumed(host, messages: ids.Count * 2);

        // Whichever came first, nothing stays held: a hold is always followed by its release.
        (await Available(client, "ITEM-4")).ShouldBe(10);
        foreach (var id in ids)
        {
            var reserved = testBus.Received<StockReserved>().Count(m => m.OrderId == id);
            if (reserved == 1)
            {
                await testBus.WaitFor<StockReleased>(m => m.OrderId == id);
            }

            reserved.ShouldBeLessThanOrEqualTo(1);
        }
    }

    /// <summary>Polls the outbox's inbox table until the service has finished this many messages.</summary>
    private static async Task WaitUntilConsumed(ServiceHost<Program> host, int messages)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            if (await db.Set<InboxState>().CountAsync(i => i.Consumed != null) >= messages)
            {
                return;
            }

            DateTime.UtcNow.ShouldBeLessThan(deadline, $"the service did not finish {messages} messages");
            await Task.Delay(50);
        }
    }

    [Fact]
    public async Task Three_copies_start_together_on_a_new_database()
    {
        // Spec 013, AC-3
        await StartTogether.ThreeCopiesMigrateOnceAsync<Program, InventoryDbContext>(await SettingsAsync());
    }

    private async Task<Dictionary<string, string?>> SettingsAsync()
    {
        var settings = infrastructure.RabbitSettings(await infrastructure.CreateVirtualHostAsync());
        settings["Persistence:Provider"] = "Postgres";
        settings["ConnectionStrings:Inventory"] = infrastructure.ConnectionString($"inventory_{Guid.NewGuid():N}");
        return settings;
    }

    private static async Task SetStock(HttpClient client, string sku, int available)
    {
        // Spec 017: only admins set stock.
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/stock/{sku}") { Content = JsonContent.Create(new SetStockRequest($"Item {sku}", 5.00m, available)) };
        request.Headers.Authorization = new("Bearer", TestTokens.Admin("admin-1"));
        var response = await client.SendAsync(request);
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
