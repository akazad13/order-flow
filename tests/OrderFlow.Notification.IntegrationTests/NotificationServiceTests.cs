using System.Net.Http.Json;
using OrderFlow.Contracts.Events;
using OrderFlow.IntegrationTesting;
using OrderFlow.Notification.Persistence;
using OrderFlow.Testing;
using OrderFlow.Notification.Inbox;

namespace OrderFlow.Notification.IntegrationTests;

/// <summary>
/// Spec 011 against real PostgreSQL and RabbitMQ. The Notification service runs in-process and is
/// restarted between steps; the test bus plays the Order and Payment services. Assertions read the
/// inbox over HTTP, so they hold for any storage.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class NotificationServiceTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task The_inbox_survives_a_restart()
    {
        // AC-1
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        var customerId = $"customer-{Guid.NewGuid():N}";
        var placed = Placed(customerId);
        var confirmed = new OrderConfirmed(placed.OrderId, customerId, 37.00m, placed.PlacedAt.AddSeconds(2));

        List<InboxMessage> before;
        await using (var first = new ServiceHost<Program>(settings))
        {
            var client = await first.StartAsync();
            var handledPlaced = HandledCounter<OrderPlaced>.On(first.Services, m => m.OrderId == placed.OrderId);
            var handledConfirmed = HandledCounter<OrderConfirmed>.On(first.Services, m => m.OrderId == placed.OrderId);
            await testBus.Publish(placed);
            await testBus.Publish(confirmed);
            await handledPlaced.WaitFor(1);
            await handledConfirmed.WaitFor(1);
            before = await Inbox(client, customerId);
        }

        before.Select(m => m.Text).ShouldBe(["We received your order", "Your order is confirmed"]);

        await using var second = new ServiceHost<Program>(settings);
        var after = await Inbox(await second.StartAsync(), customerId);

        after.ShouldBe(before, "the inbox changed across the restart");
    }

    [Fact]
    public async Task A_repeat_after_a_restart_adds_nothing()
    {
        // AC-2: the original message stays; no new one (a new one would have a new ID).
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        var customerId = $"customer-{Guid.NewGuid():N}";
        var placed = Placed(customerId);

        List<InboxMessage> before;
        await using (var first = new ServiceHost<Program>(settings))
        {
            var client = await first.StartAsync();
            var handled = HandledCounter<OrderPlaced>.On(first.Services, m => m.OrderId == placed.OrderId);
            await testBus.Publish(placed);
            await handled.WaitFor(1);
            before = await Inbox(client, customerId);
        }

        await using var second = new ServiceHost<Program>(settings);
        var restarted = await second.StartAsync();
        var repeat = HandledCounter<OrderPlaced>.On(second.Services, m => m.OrderId == placed.OrderId);
        await testBus.Publish(placed);
        await repeat.WaitFor(1);

        (await Inbox(restarted, customerId)).ShouldBe(before, "the customer was told again after the restart");
    }

    [Fact]
    public async Task Copies_at_the_same_moment_tell_once()
    {
        // AC-3: four copies of each order's placement at once.
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        await using var host = new ServiceHost<Program>(settings);
        var client = await host.StartAsync();

        var customerId = $"customer-{Guid.NewGuid():N}";
        var orders = Enumerable.Range(0, 10).Select(_ => Placed(customerId)).ToList();
        var ids = orders.Select(o => o.OrderId).ToHashSet();
        const int copies = 4;

        var handled = HandledCounter<OrderPlaced>.On(host.Services, m => ids.Contains(m.OrderId));
        await Task.WhenAll(orders.SelectMany(o => Enumerable.Range(0, copies).Select(_ => testBus.Publish(o))));
        await handled.WaitFor(orders.Count * copies);

        var inbox = await Inbox(client, customerId);
        inbox.Count.ShouldBe(orders.Count);
        inbox.Select(m => m.OrderId).ShouldBe(ids, ignoreOrder: true);
    }

    private static OrderPlaced Placed(string customerId) =>
        new(Guid.NewGuid(), customerId, [new OrderPlacedLine("ITEM-1", 1)], DateTimeOffset.UtcNow);

    private static async Task<List<InboxMessage>> Inbox(HttpClient client, string customerId) =>
        (await client.LoggedInAs(customerId).GetFromJsonAsync<List<InboxMessage>>($"/inbox/{customerId}"))!;

    [Fact]
    public async Task Three_copies_start_together_on_a_new_database()
    {
        // Spec 013, AC-3
        await StartTogether.ThreeCopiesMigrateOnceAsync<Program, NotificationDbContext>(await SettingsAsync());
    }

    private async Task<Dictionary<string, string?>> SettingsAsync()
    {
        var settings = infrastructure.RabbitSettings(await infrastructure.CreateVirtualHostAsync());
        settings["Persistence:Provider"] = "Postgres";
        settings["ConnectionStrings:Notifications"] = infrastructure.ConnectionString($"notifications_{Guid.NewGuid():N}");
        return settings;
    }
}
