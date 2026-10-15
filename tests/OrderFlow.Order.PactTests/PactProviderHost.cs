using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using MassTransit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Contracts.Events;
using OrderFlow.OrderService.Api;
using OrderFlow.OrderService.Domain;
using OrderFlow.OrderService.Persistence;
using OrderFlow.Testing;

namespace OrderFlow.OrderService.PactTests;

/// <summary>
/// The real Order service on a real port (Pact's verifier makes HTTP calls), with in-memory
/// storage and the MassTransit test harness instead of RabbitMQ. Adds the test-only
/// provider-state endpoint; the service itself never exposes it (ADR 0006).
/// </summary>
public sealed class PactProviderHost : WebApplicationFactory<Program>
{
    private readonly string? _renameStatusTo;

    /// <param name="renameStatusTo">
    /// Breaks the API on purpose: the order's <c>status</c> field is written under this name
    /// instead (spec 008, AC-5). Null for the real API.
    /// </param>
    public PactProviderHost(string? renameStatusTo = null)
    {
        _renameStatusTo = renameStatusTo;
        Port = FreeTcpPort();
        UseKestrel(Port);
    }

    public int Port { get; }

    public Uri BaseUri => new($"http://localhost:{Port}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.AddMassTransitTestHarness();
            services.TrustTestTokens(); // spec 012
            services.AddSingleton<IStartupFilter, ProviderStateStartupFilter>();

            if (_renameStatusTo is { } name)
            {
                services.ConfigureHttpJsonOptions(json => json.SerializerOptions.TypeInfoResolver = new DefaultJsonTypeInfoResolver
                {
                    Modifiers =
                    {
                        type =>
                        {
                            if (type.Type != typeof(OrderResponse)) return;
                            foreach (var property in type.Properties.Where(p => p.Name == "status")) property.Name = name;
                        },
                    },
                });
            }
        });
    }

    private static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}

/// <summary>
/// POST /provider-states: puts the service into the state an interaction needs, using the
/// domain, and returns values the contract injects into the request (e.g. orderId).
/// </summary>
internal sealed class ProviderStateStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Map("/provider-states", states => states.Run(SetUpState));
        next(app);
    };

    private static async Task SetUpState(HttpContext context)
    {
        var request = await JsonSerializer.DeserializeAsync<ProviderStateRequest>(context.Request.Body, JsonSerializerOptions.Web);
        var orders = context.RequestServices.GetRequiredService<IOrderRepository>();

        Order? order = request?.Action == "teardown" ? null : request?.State switch
        {
            "a pending order exists" => Placed(),
            "a confirmed order exists" => Confirmed(DateTimeOffset.UtcNow),
            "an order confirmed too long ago to cancel exists" => Confirmed(DateTimeOffset.UtcNow.AddDays(-2)), // spec 022
            "an order cancelled for a shortage exists" => CancelledForShortage(),
            _ => null,
        };

        if (order is not null)
        {
            await orders.AddAsync(order, context.RequestAborted);
            await orders.SaveChangesAsync(context.RequestAborted);
        }

        await context.Response.WriteAsJsonAsync(order is null ? new Dictionary<string, object>() : new Dictionary<string, object> { ["orderId"] = order.Id });
    }

    private static Order Placed() => Order.Place("customer-1", [new OrderLine("SKU-001", 2)], TimeProvider.System).Order!;

    private static Order Confirmed(DateTimeOffset at)
    {
        var order = Placed();
        order.RecordStockHeld(37.00m);
        order.Confirm(at, TimeSpan.FromHours(24));
        return order;
    }

    private static Order CancelledForShortage()
    {
        var order = Order.Place("customer-1", [new OrderLine("SKU-002", 2)], TimeProvider.System).Order!;
        order.Cancel(CancellationReasons.OutOfStock, "Only 1 of SKU-002 available, 2 requested.");
        return order;
    }

    private sealed record ProviderStateRequest(string? State, string? Action);
}
