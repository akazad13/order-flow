using System.Net;
using System.Net.Http.Json;
using MassTransit.EntityFrameworkCoreIntegration;
using OrderFlow.Contracts.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.IntegrationTesting;
using OrderFlow.Testing;
using OrderFlow.OrderService.Api;
using OrderFlow.OrderService.Persistence;
using OrderFlow.OrderService.StateMachine;
using Xunit.Abstractions;

namespace OrderFlow.Order.IntegrationTests;

/// <summary>
/// Spec 007 against real PostgreSQL and RabbitMQ. The Order service runs in-process; the
/// test bus plays Inventory and Payment.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class OrderServiceTests(InfrastructureFixture infrastructure, ITestOutputHelper output)
{
    [Fact]
    public async Task Order_and_its_progress_survive_a_restart()
    {
        // AC-1
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);

        Guid orderId;
        await using (var before = new ServiceHost<Program>(settings))
        {
            var client = (await before.StartAsync()).LoggedInAs("customer-1");
            orderId = (await PlaceOrder(client, "SKU-001", 2)).OrderId;
            await WaitUntilTracked(before, orderId);
        }

        await using var after = new ServiceHost<Program>(settings);
        var restarted = (await after.StartAsync()).LoggedInAs("customer-1");

        var order = await restarted.GetFromJsonAsync<OrderResponse>($"/orders/{orderId}");
        order!.Status.ShouldBe("Pending");

        // The state machine picks up where it left off.
        await testBus.Publish(new StockReserved(orderId, "customer-1", [new StockReservedLine("SKU-001", 2, 18.50m)], 37.00m, DateTimeOffset.UtcNow));
        await testBus.Publish(new PaymentSucceeded(orderId, Guid.NewGuid(), 37.00m, DateTimeOffset.UtcNow));
        await testBus.WaitFor<OrderConfirmed>(m => m.OrderId == orderId);

        order = await restarted.GetFromJsonAsync<OrderResponse>($"/orders/{orderId}");
        order!.Status.ShouldBe("Confirmed");
        order.Total.ShouldBe(37.00m);
    }

    [Fact]
    public async Task Order_placed_while_the_broker_is_down_is_announced_when_it_is_back()
    {
        // AC-3
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        await using var host = new ServiceHost<Program>(settings);
        var client = (await host.StartAsync()).LoggedInAs("customer-1");

        await infrastructure.Rabbit.StopAsync();
        Guid orderId;
        try
        {
            orderId = (await PlaceOrder(client, "SKU-002", 1)).OrderId;
            (await client.GetFromJsonAsync<OrderResponse>($"/orders/{orderId}"))!.Status.ShouldBe("Pending");
        }
        finally
        {
            await infrastructure.Rabbit.StartAsync();
        }

        // Nobody retries by hand: the outbox delivers once the broker is reachable.
        await testBus.WaitFor<OrderPlaced>(m => m.OrderId == orderId, TimeSpan.FromSeconds(90));
    }

    [Fact]
    public async Task Thirty_orders_at_once_all_confirm()
    {
        // AC-5
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        testBus.React<OrderPlaced>((m, bus) => bus.Publish(new StockReserved(m.OrderId, m.CustomerId, [], 10.00m, DateTimeOffset.UtcNow)));
        testBus.React<StockReserved>((m, bus) => bus.Publish(new PaymentSucceeded(m.OrderId, Guid.NewGuid(), m.Total, DateTimeOffset.UtcNow)));

        await using var host = new ServiceHost<Program>(settings);
        var client = (await host.StartAsync()).LoggedInAs("customer-1");

        var placed = await Task.WhenAll(Enumerable.Range(0, 30).Select(_ => PlaceOrder(client, "SKU-003", 1)));
        var ids = placed.Select(o => o.OrderId).ToHashSet();

        await testBus.WaitForCount<OrderConfirmed>(m => ids.Contains(m.OrderId), ids.Count, TimeSpan.FromSeconds(60));
        foreach (var id in ids)
        {
            (await client.GetFromJsonAsync<OrderResponse>($"/orders/{id}"))!.Status.ShouldBe("Confirmed");
        }
    }

    [Fact]
    public async Task Cancellation_and_payment_racing_end_one_way()
    {
        // Spec 006, AC-6: for each order, the customer cancels at the moment the payment succeeds.
        // Since spec 022 a confirmed order can still be cancelled (within 24 hours), so every order
        // ends Cancelled: before it was confirmed, or right after.
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        testBus.React<OrderPlaced>((m, bus) => bus.Publish(new StockReserved(m.OrderId, m.CustomerId, [], 10.00m, DateTimeOffset.UtcNow)));

        await using var host = new ServiceHost<Program>(settings);
        var client = (await host.StartAsync()).LoggedInAs("customer-1");

        var placed = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PlaceOrder(client, "SKU-003", 1)));
        var ids = placed.Select(o => o.OrderId).ToHashSet();
        await WaitUntil(host, db => db.Set<OrderState>().CountAsync(s => ids.Contains(s.CorrelationId) && s.CurrentState == "AwaitingPayment"), ids.Count, "every order awaiting payment");

        // Half the payments are sent with the request; the request travels through the database
        // and the outbox, so the payment usually wins. The other half are sent once the request is
        // on the broker, so the request usually wins. Either way, each order must end one way.
        var answers = await Task.WhenAll(ids.Select(async (id, i) =>
        {
            var cancel = client.PostAsync($"/orders/{id}/cancellation", null);
            if (i % 2 == 1)
            {
                await testBus.WaitFor<OrderCancellationRequested>(m => m.OrderId == id);
            }

            await testBus.Publish(new PaymentSucceeded(id, Guid.NewGuid(), 10.00m, DateTimeOffset.UtcNow));
            return (await cancel).StatusCode;
        }));
        answers.ShouldAllBe(code => code == HttpStatusCode.Accepted);

        // Settled: every order cancelled and nothing left to send, so no further event can appear.
        await WaitUntil(host, db => db.Set<OrderState>().CountAsync(s => ids.Contains(s.CorrelationId) && s.CurrentState == "Cancelled"), ids.Count, "every order cancelled");
        await WaitUntil(host, async db => await db.Set<OutboxMessage>().AnyAsync() ? 0 : 1, 1, "the outbox drained");
        await testBus.WaitForCount<OrderCancelledV2>(m => ids.Contains(m.OrderId), ids.Count);

        foreach (var id in ids)
        {
            var confirmed = testBus.Received<OrderConfirmed>().Count(m => m.OrderId == id);
            var cancelled = testBus.Received<OrderCancelledV2>().Count(m => m.OrderId == id);
            cancelled.ShouldBe(1, $"order {id}: cancelled {cancelled} times");
            confirmed.ShouldBeLessThanOrEqualTo(1, $"order {id}: confirmed {confirmed} times");

            var order = await client.GetFromJsonAsync<OrderResponse>($"/orders/{id}");
            order!.Status.ShouldBe("Cancelled");
        }

        output.WriteLine($"Confirmed, then cancelled: {testBus.Received<OrderConfirmed>().Count(m => ids.Contains(m.OrderId))}; all cancelled: {testBus.Received<OrderCancelledV2>().Count(m => ids.Contains(m.OrderId))}.");
    }

    /// <summary>Polls a count in the service's database until it reaches the target.</summary>
    private static async Task WaitUntil(ServiceHost<Program> host, Func<OrderDbContext, Task<int>> count, int target, string what)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (true)
        {
            await using var scope = host.Services.CreateAsyncScope();
            if (await count(scope.ServiceProvider.GetRequiredService<OrderDbContext>()) >= target)
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(50);
        }
    }

    /// <summary>Polls the database until the state machine has stored the order's progress.</summary>
    private static async Task WaitUntilTracked(ServiceHost<Program> host, Guid orderId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderDbContext>();
            if (await db.Set<OrderState>().AnyAsync(s => s.CorrelationId == orderId))
            {
                return;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Order {orderId} was never tracked by the state machine.");
            }

            await Task.Delay(50);
        }
    }

    private async Task<Dictionary<string, string?>> SettingsAsync()
    {
        var settings = infrastructure.RabbitSettings(await infrastructure.CreateVirtualHostAsync());
        settings["Persistence:Provider"] = "Postgres";
        settings["ConnectionStrings:Orders"] = infrastructure.ConnectionString($"orders_{Guid.NewGuid():N}");
        return settings;
    }

    private static async Task<OrderResponse> PlaceOrder(HttpClient client, string sku, int quantity)
    {
        var response = await client.PostAsJsonAsync("/orders", new PlaceOrderRequest([new OrderLineDto(sku, quantity)]));
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<OrderResponse>())!;
    }
}
