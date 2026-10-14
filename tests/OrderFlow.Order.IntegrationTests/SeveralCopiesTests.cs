using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using OrderFlow.Contracts.Events;
using OrderFlow.IntegrationTesting;
using OrderFlow.OrderService.Api;
using OrderFlow.OrderService.Persistence;
using OrderFlow.Testing;

namespace OrderFlow.Order.IntegrationTests;

/// <summary>
/// Spec 013: two copies of the Order service on one database and one broker. The test bus plays
/// Inventory and Payment.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SeveralCopiesTests(InfrastructureFixture infrastructure)
{
    private static readonly TimeSpan PushTimeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Watchers_on_either_copy_see_the_final_status()
    {
        // AC-1: each order is watched on both copies; with one shared queue, only one copy would hear each change.
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        await using var first = new ServiceHost<Program>(settings);
        await using var second = new ServiceHost<Program>(settings);
        var client = (await first.StartAsync()).LoggedInAs("customer-1");
        await second.StartAsync();

        for (var i = 0; i < 4; i++)
        {
            var response = await client.PostAsJsonAsync("/orders", new PlaceOrderRequest([new OrderLineDto("SKU-013", 1)]));
            response.StatusCode.ShouldBe(HttpStatusCode.Created);
            var orderId = (await response.Content.ReadFromJsonAsync<OrderResponse>())!.OrderId;
            await testBus.WaitFor<OrderPlaced>(m => m.OrderId == orderId);

            await using var onFirst = await Watch(first, orderId);
            await using var onSecond = await Watch(second, orderId);

            await testBus.Publish(new StockReserved(orderId, "customer-1", [new StockReservedLine("SKU-013", 1, 10.00m)], 10.00m, DateTimeOffset.UtcNow));
            await testBus.Publish(new PaymentSucceeded(orderId, Guid.NewGuid(), 10.00m, DateTimeOffset.UtcNow));

            (await onFirst.NextStatus()).ShouldBe("Confirmed", $"order {i + 1}: the watcher on the first copy");
            (await onSecond.NextStatus()).ShouldBe("Confirmed", $"order {i + 1}: the watcher on the second copy");
        }
    }

    [Fact]
    public async Task Three_copies_start_together_on_a_new_database()
    {
        // AC-3
        await StartTogether.ThreeCopiesMigrateOnceAsync<Program, OrderDbContext>(await SettingsAsync());
    }

    /// <summary>A browser-like watcher (WebSocket only, token in the query string) on one copy.</summary>
    private static async Task<Watcher> Watch(ServiceHost<Program> copy, Guid orderId)
    {
        var server = copy.Server;
        var token = TestTokens.For("customer-1");
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/orders"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancellationToken) =>
                    await server.CreateWebSocketClient().ConnectAsync(new Uri($"{context.Uri}?access_token={token}"), cancellationToken);
            })
            .Build();

        var watcher = new Watcher(connection);
        connection.On<JsonElement>("OrderUpdated", update => watcher.Updates.Writer.TryWrite(update));
        await connection.StartAsync();
        await connection.InvokeAsync("WatchOrder", orderId);
        return watcher;
    }

    private sealed class Watcher(HubConnection connection) : IAsyncDisposable
    {
        public Channel<JsonElement> Updates { get; } = Channel.CreateUnbounded<JsonElement>();

        public async Task<string?> NextStatus()
        {
            using var timeout = new CancellationTokenSource(PushTimeout);
            try
            {
                return (await Updates.Reader.ReadAsync(timeout.Token)).GetProperty("status").GetString();
            }
            catch (OperationCanceledException)
            {
                return "(nothing pushed)";
            }
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private async Task<Dictionary<string, string?>> SettingsAsync()
    {
        var settings = infrastructure.RabbitSettings(await infrastructure.CreateVirtualHostAsync());
        settings["Persistence:Provider"] = "Postgres";
        settings["ConnectionStrings:Orders"] = infrastructure.ConnectionString($"orders_{Guid.NewGuid():N}");
        return settings;
    }
}
