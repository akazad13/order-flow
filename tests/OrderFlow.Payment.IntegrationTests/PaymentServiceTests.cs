using OrderFlow.Contracts.Events;
using OrderFlow.IntegrationTesting;

namespace OrderFlow.Payment.IntegrationTests;

/// <summary>
/// Spec 010 against real PostgreSQL and RabbitMQ. The Payment service runs in-process and is
/// restarted between steps; the test bus plays Inventory and Order. Assertions use only what
/// Payment announces, so they hold for any storage.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class PaymentServiceTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task A_repeat_after_a_restart_charges_nothing()
    {
        // AC-1
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        var reserved = StockReservedFor(Guid.NewGuid(), 37.00m);

        await using (var before = new ServiceHost<Program>(settings))
        {
            await before.StartAsync();
            await testBus.Publish(reserved);
            await testBus.WaitFor<PaymentSucceeded>(m => m.OrderId == reserved.OrderId);
        }

        await using var after = new ServiceHost<Program>(settings);
        await after.StartAsync();
        var handled = Handled<StockReserved>(after, m => m.OrderId == reserved.OrderId);
        await testBus.Publish(reserved);
        await handled.WaitFor(1);
        await Settle(testBus);

        testBus.Received<PaymentSucceeded>().Count(m => m.OrderId == reserved.OrderId).ShouldBe(1, "charged again after the restart");
    }

    [Fact]
    public async Task A_void_survives_a_restart()
    {
        // AC-2: cancelled before it was charged.
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        var orderId = Guid.NewGuid();

        await using (var before = new ServiceHost<Program>(settings))
        {
            await before.StartAsync();
            var cancelled = Handled<OrderCancelled>(before, m => m.OrderId == orderId);
            await testBus.Publish(CancelledAtCustomerRequest(orderId));
            await cancelled.WaitFor(1);
        }

        await using var after = new ServiceHost<Program>(settings);
        await after.StartAsync();
        var handled = Handled<StockReserved>(after, m => m.OrderId == orderId);
        await testBus.Publish(StockReservedFor(orderId, 37.00m));
        await handled.WaitFor(1);
        await Settle(testBus);

        testBus.Received<PaymentSucceeded>().ShouldNotContain(m => m.OrderId == orderId, "a voided order was charged after the restart");
    }

    [Fact]
    public async Task A_refund_after_a_restart_happens_once()
    {
        // AC-2: charged, restart, cancelled; restart again, cancelled again.
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        var reserved = StockReservedFor(Guid.NewGuid(), 37.00m);

        await using (var first = new ServiceHost<Program>(settings))
        {
            await first.StartAsync();
            await testBus.Publish(reserved);
            await testBus.WaitFor<PaymentSucceeded>(m => m.OrderId == reserved.OrderId);
        }

        await using (var second = new ServiceHost<Program>(settings))
        {
            await second.StartAsync();
            await testBus.Publish(CancelledAtCustomerRequest(reserved.OrderId));
            var refund = await testBus.WaitFor<PaymentRefunded>(m => m.OrderId == reserved.OrderId);
            refund.Amount.ShouldBe(37.00m);
        }

        await using var third = new ServiceHost<Program>(settings);
        await third.StartAsync();
        var handled = Handled<OrderCancelled>(third, m => m.OrderId == reserved.OrderId);
        await testBus.Publish(CancelledAtCustomerRequest(reserved.OrderId));
        await handled.WaitFor(1);
        await Settle(testBus);

        testBus.Received<PaymentRefunded>().Count(m => m.OrderId == reserved.OrderId).ShouldBe(1, "refunded again after the restart");
    }

    [Fact]
    public async Task Copies_at_the_same_moment_charge_and_refund_once()
    {
        // AC-3, AC-4: for each order, copies of the stock-held news at once, then copies of the
        // cancellation at once. A copy that loses a race has already decided and published; its
        // announcement must vanish with its failed save. Losing copies show up in the log as
        // retried conflicts: "PK_payments" for charges, an xmin mismatch for refunds.
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        await using var host = new ServiceHost<Program>(settings);
        await host.StartAsync();

        var orders = Enumerable.Range(0, 10).Select(_ => StockReservedFor(Guid.NewGuid(), 10.00m)).ToList();
        var ids = orders.Select(o => o.OrderId).ToHashSet();

        const int copies = 4;
        var charged = Handled<StockReserved>(host, m => ids.Contains(m.OrderId));
        await Task.WhenAll(orders.SelectMany(o => Enumerable.Range(0, copies).Select(_ => testBus.Publish(o))));
        await charged.WaitFor(orders.Count * copies);

        var cancelled = Handled<OrderCancelled>(host, m => ids.Contains(m.OrderId));
        await Task.WhenAll(orders.SelectMany(o => Enumerable.Range(0, copies).Select(_ => testBus.Publish(CancelledAtCustomerRequest(o.OrderId)))));
        await cancelled.WaitFor(orders.Count * copies);
        await Settle(testBus);

        foreach (var id in ids)
        {
            testBus.Received<PaymentSucceeded>().Count(m => m.OrderId == id).ShouldBe(1, $"charges announced for {id}");
            testBus.Received<PaymentRefunded>().Count(m => m.OrderId == id).ShouldBe(1, $"refunds announced for {id}");
        }
    }

    /// <summary>
    /// Anything Payment announced before now has reached the test bus once a fresh order's charge
    /// (published after it) has: a deterministic "nothing else is on its way".
    /// </summary>
    private static async Task Settle(TestBus testBus)
    {
        var probe = StockReservedFor(Guid.NewGuid(), 1.00m);
        await testBus.Publish(probe);
        await testBus.WaitFor<PaymentSucceeded>(m => m.OrderId == probe.OrderId);
    }

    private static HandledCounter<T> Handled<T>(ServiceHost<Program> host, Func<T, bool> match) where T : class =>
        HandledCounter<T>.On(host.Services, match);

    private static StockReserved StockReservedFor(Guid orderId, decimal total) =>
        new(orderId, "customer-1", [new StockReservedLine("ITEM-1", 1, total)], total, DateTimeOffset.UtcNow);

    private static OrderCancelled CancelledAtCustomerRequest(Guid orderId) =>
        new(orderId, "customer-1", CancellationReasons.CustomerRequested, "Cancelled at the customer's request.", DateTimeOffset.UtcNow);

    private async Task<Dictionary<string, string?>> SettingsAsync()
    {
        var settings = infrastructure.RabbitSettings(await infrastructure.CreateVirtualHostAsync());
        settings["Persistence:Provider"] = "Postgres";
        settings["ConnectionStrings:Payments"] = infrastructure.ConnectionString($"payments_{Guid.NewGuid():N}");
        return settings;
    }
}
