using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace OrderFlow.IntegrationTesting;

/// <summary>
/// Counts messages a service under test finished handling (successfully or not), so a test can
/// wait for "the repeat was processed" even when processing it announces nothing (specs 010, 011).
/// </summary>
public sealed class HandledCounter<T>(Func<T, bool> match) : IConsumeMessageObserver<T> where T : class
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(30);

    private int _count;

    /// <summary>Starts counting the matching messages the service behind <paramref name="services"/> handles.</summary>
    public static HandledCounter<T> On(IServiceProvider services, Func<T, bool> match)
    {
        var counter = new HandledCounter<T>(match);

        // Resolve the bus off the test's execution flow: resolving it sets MassTransit's ambient
        // log context (an AsyncLocal), which would outlive this host and break the next one
        // started in the same test (a "restart"; found in spec 010).
        Task.Run(() => services.GetRequiredService<IBus>().ConnectConsumeMessageObserver(counter)).GetAwaiter().GetResult();
        return counter;
    }

    public Task PreConsume(ConsumeContext<T> context) => Task.CompletedTask;

    public Task PostConsume(ConsumeContext<T> context)
    {
        if (match(context.Message)) Interlocked.Increment(ref _count);
        return Task.CompletedTask;
    }

    public Task ConsumeFault(ConsumeContext<T> context, Exception exception) => Task.CompletedTask;

    /// <summary>Polls until the service has handled this many matching messages.</summary>
    public async Task WaitFor(int count, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (Volatile.Read(ref _count) < count)
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"The service handled {_count} of {count} {typeof(T).Name} within {timeout ?? DefaultTimeout}.");
            }

            await Task.Delay(50);
        }
    }
}
