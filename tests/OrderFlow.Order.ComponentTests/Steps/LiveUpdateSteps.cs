using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Channels;
using MassTransit.Testing;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using OrderFlow.Contracts.Events;
using OrderFlow.OrderService.Api;
using OrderFlow.OrderService.ComponentTests.Support;
using OrderFlow.Testing;
using Reqnroll;
using Shouldly;

namespace OrderFlow.OrderService.ComponentTests.Steps;

/// <summary>
/// Spec 005: a real SignalR client, over WebSockets on the in-memory test server, watching
/// the scenario's order.
/// </summary>
[Binding]
public sealed class LiveUpdateSteps(OrderServiceFactory service, OrderLifecycleSteps lifecycle)
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly Channel<JsonElement> _updates = Channel.CreateUnbounded<JsonElement>();
    private HubConnection? _connection;
    private JsonElement _lastUpdate;
    private OrderResponse? _other;

    [Given("a client is watching that order")]
    public async Task GivenAClientIsWatchingThatOrder()
    {
        var server = service.Server;
        _connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/orders"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.WebSocketFactory = async (context, cancellationToken) =>
                    await server.CreateWebSocketClient().ConnectAsync(context.Uri, cancellationToken);
            })
            .Build();

        _connection.On<JsonElement>("OrderUpdated", update => _updates.Writer.TryWrite(update));
        await _connection.StartAsync();
        await _connection.InvokeAsync("WatchOrder", lifecycle.Placed.OrderId);
    }

    [Given("customer {string} has placed another order for {int} x {string}")]
    public async Task GivenAnotherOrder(string customerId, int quantity, string sku)
    {
        var response = await service.CreateClient().PostAsJsonAsync("/orders", new PlaceOrderRequest(customerId, [new OrderLineDto(sku, quantity)]));
        response.EnsureSuccessStatusCode();
        _other = await response.Content.ReadFromJsonAsync<OrderResponse>();
    }

    [When("that other order is confirmed")]
    public async Task WhenThatOtherOrderIsConfirmed()
    {
        var other = _other!;
        await service.Harness.Bus.Publish(new StockReserved(other.OrderId, other.CustomerId, [], 10.00m, DateTimeOffset.UtcNow));
        await service.Harness.Bus.Publish(new PaymentSucceeded(other.OrderId, Guid.NewGuid(), 10.00m, DateTimeOffset.UtcNow));

        // Wait until the service has handled the confirmation (and so pushed, if it ever would).
        await Eventually.True(
            () => service.Harness.Consumed.Select<OrderConfirmed>(m => m.Context.Message.OrderId == other.OrderId).Any(),
            "the other order's OrderConfirmed handled by the Order service");
    }

    [Then("the client is sent that order with status {string}")]
    public async Task ThenTheClientIsSentThatOrder(string status)
    {
        using var timeout = new CancellationTokenSource(Timeout);
        try
        {
            _lastUpdate = await _updates.Reader.ReadAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            throw new ShouldAssertException($"No update was sent within {Timeout.TotalSeconds} seconds.");
        }

        _lastUpdate.GetProperty("orderId").GetGuid().ShouldBe(lifecycle.Placed.OrderId);
        _lastUpdate.GetProperty("status").GetString().ShouldBe(status);
    }

    [Then("the update is exactly what reading the order returns")]
    public async Task ThenTheUpdateIsWhatReadingReturns()
    {
        var read = await service.CreateClient().GetFromJsonAsync<JsonElement>($"/orders/{lifecycle.Placed.OrderId}");

        JsonElement.DeepEquals(_lastUpdate, read).ShouldBeTrue($"pushed {_lastUpdate} but read {read}");
    }

    [Then("the client was sent nothing else")]
    public void ThenTheClientWasSentNothingElse()
    {
        // Pushes go out in order on one connection: one for the other order would be here by now.
        _updates.Reader.TryRead(out var extra).ShouldBeFalse($"unexpected update {extra}");
    }

    [AfterScenario]
    public async Task StopWatching()
    {
        if (_connection is not null)
        {
            await _connection.DisposeAsync();
        }
    }
}
