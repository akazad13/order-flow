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
using Microsoft.AspNetCore.SignalR;
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
        _connection = Connect(TestTokens.For(lifecycle.Placed.CustomerId));
        _connection.On<JsonElement>("OrderUpdated", update => _updates.Writer.TryWrite(update));
        await _connection.StartAsync();
        await _connection.InvokeAsync("WatchOrder", lifecycle.Placed.OrderId);
    }

    [Given("staff member {string} is watching that order")]
    public async Task GivenStaffIsWatchingThatOrder(string userId)
    {
        // Spec 015, AC-3.
        _connection = Connect(TestTokens.Staff(userId));
        _connection.On<JsonElement>("OrderUpdated", update => _updates.Writer.TryWrite(update));
        await _connection.StartAsync();
        await _connection.InvokeAsync("WatchOrder", lifecycle.Placed.OrderId);
    }

    [Then("a client that is not logged in cannot connect")]
    public async Task ThenAClientThatIsNotLoggedInCannotConnect()
    {
        // Spec 012, AC-1.
        await using var anonymous = Connect(token: null);
        var refused = await Should.ThrowAsync<Exception>(anonymous.StartAsync());
        refused.ToString().ShouldContain("401");
    }

    [Then("customer {string} cannot watch that order")]
    public async Task ThenCustomerCannotWatchThatOrder(string customerId)
    {
        // Spec 012, AC-3: refused exactly as for an order that does not exist.
        await using var other = Connect(TestTokens.For(customerId));
        await other.StartAsync();

        var refused = await Should.ThrowAsync<HubException>(other.InvokeAsync("WatchOrder", lifecycle.Placed.OrderId));
        var unknown = await Should.ThrowAsync<HubException>(other.InvokeAsync("WatchOrder", Guid.NewGuid()));
        refused.Message.ShouldContain("Order not found.");
        refused.Message.ShouldBe(unknown.Message);
    }

    /// <summary>A real SignalR client over WebSockets on the test server; the token goes in the query string, as in the browser.</summary>
    private HubConnection Connect(string? token)
    {
        var server = service.Server;
        return new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, "hubs/orders"), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.SkipNegotiation = true;

                // As a browser does: a WebSocket cannot carry an Authorization header, so the token
                // travels as ?access_token= (the .NET client leaves that to a custom factory).
                options.WebSocketFactory = async (context, cancellationToken) =>
                {
                    var uri = token is null ? context.Uri : new Uri($"{context.Uri}{(context.Uri.Query.Length > 0 ? "&" : "?")}access_token={token}");
                    return await server.CreateWebSocketClient().ConnectAsync(uri, cancellationToken);
                };
            })
            .Build();
    }

    [Given("customer {string} has placed another order for {int} x {string}")]
    public async Task GivenAnotherOrder(string customerId, int quantity, string sku)
    {
        var response = await service.ClientFor(customerId).PostAsJsonAsync("/orders", new PlaceOrderRequest([new OrderLineDto(sku, quantity)]));
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
        var read = await service.ClientFor(lifecycle.Placed.CustomerId).GetFromJsonAsync<JsonElement>($"/orders/{lifecycle.Placed.OrderId}");

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
