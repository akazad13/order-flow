using System.Collections.Concurrent;
using MassTransit;
using OrderFlow.Contracts.Events;

namespace OrderFlow.IntegrationTesting;

/// <summary>
/// A bus of the test's own on the real broker. It records every event it sees and can play
/// the part of the services that are not under test (e.g. reply to OrderPlaced with
/// StockReserved).
/// </summary>
public sealed class TestBus : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private readonly ConcurrentQueue<object> _received = new();
    private readonly ConcurrentDictionary<Type, Func<object, ConsumeContext, Task>> _reactions = new();
    private readonly IBusControl _bus;

    private TestBus(InfrastructureFixture infrastructure, string vhost)
    {
        var (user, password) = infrastructure.RabbitCredentials;
        _bus = Bus.Factory.CreateUsingRabbitMq(cfg =>
        {
            cfg.Host("localhost", infrastructure.RabbitPort, vhost, h =>
            {
                h.Username(user);
                h.Password(password);
            });

            cfg.ReceiveEndpoint("test-observer", e =>
            {
                Observe<OrderPlaced>(e);
                Observe<StockReserved>(e);
                Observe<StockRejected>(e);
                Observe<StockReleased>(e);
                Observe<PaymentSucceeded>(e);
                Observe<PaymentFailed>(e);
                Observe<OrderConfirmed>(e);
                Observe<OrderCancelled>(e);
            });
        });
    }

    public static async Task<TestBus> StartAsync(InfrastructureFixture infrastructure, string vhost)
    {
        var testBus = new TestBus(infrastructure, vhost);
        await testBus._bus.StartAsync();
        return testBus;
    }

    /// <summary>Reply to every <typeparamref name="T"/> this bus sees, e.g. to stand in for Inventory.</summary>
    public void React<T>(Func<T, IPublishEndpoint, Task> reaction) where T : class =>
        _reactions[typeof(T)] = (message, context) => reaction((T)message, context);

    public Task Publish<T>(T message) where T : class => _bus.Publish(message);

    public IReadOnlyList<T> Received<T>() => _received.OfType<T>().ToList();

    /// <summary>Polls what has been received until a match arrives or the timeout passes.</summary>
    public async Task<T> WaitFor<T>(Func<T, bool> match, TimeSpan? timeout = null)
    {
        var results = await WaitForCount(match, 1, timeout);
        return results[0];
    }

    public async Task<IReadOnlyList<T>> WaitForCount<T>(Func<T, bool> match, int count, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            var matches = _received.OfType<T>().Where(match).ToList();
            if (matches.Count >= count)
            {
                return matches;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Expected {count} {typeof(T).Name} message(s), received {matches.Count} within {timeout ?? DefaultTimeout}.");
            }

            await Task.Delay(50);
        }
    }

    public async ValueTask DisposeAsync() => await _bus.StopAsync();

    private void Observe<T>(IReceiveEndpointConfigurator endpoint) where T : class =>
        endpoint.Handler<T>(async context =>
        {
            _received.Enqueue(context.Message);
            if (_reactions.TryGetValue(typeof(T), out var reaction))
            {
                await reaction(context.Message, context);
            }
        });
}
