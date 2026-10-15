using Microsoft.Extensions.Options;

namespace OrderFlow.Gateway.Sessions;

/// <summary>A session store that can remove the sessions whose expiry has passed (spec 024).</summary>
public interface IExpiredSessions
{
    /// <summary>Removes the sessions that expired by <paramref name="now"/>; returns how many.</summary>
    Task<int> RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

/// <summary>
/// Every <see cref="GatewayOptions.SessionCleanupEvery"/>, removes the expired sessions (spec 024, AC-1).
/// Every copy runs it, without a lock: a second removal finds nothing (Q2). A failure is logged and
/// the next cleanup runs on time (AC-3).
/// </summary>
public sealed partial class ExpiredSessionsCleanup(
    IExpiredSessions sessions,
    IOptions<GatewayOptions> options,
    TimeProvider clock,
    ILogger<ExpiredSessionsCleanup> logger) : BackgroundService
{
    private TaskCompletionSource<int> _next = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private PeriodicTimer? _timer;

    /// <summary>Completes when the next cleanup ends: with how many sessions it removed, or its failure.</summary>
    public Task<int> NextCleanup => Volatile.Read(ref _next).Task;

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        // The schedule starts with the gateway, not whenever ExecuteAsync gets a thread.
        _timer = new PeriodicTimer(options.Value.SessionCleanupEvery, clock);
        return base.StartAsync(cancellationToken);
    }

    public override void Dispose()
    {
        _timer?.Dispose();
        base.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (await _timer!.WaitForNextTickAsync(stoppingToken))
        {
            var done = Interlocked.Exchange(ref _next, new(TaskCreationOptions.RunContinuationsAsynchronously));
            try
            {
                var removed = await sessions.RemoveExpiredAsync(clock.GetUtcNow(), stoppingToken);
                if (removed > 0)
                {
                    Removed(removed);
                }

                done.SetResult(removed);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                Failed(error);
                done.SetException(error);
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Removed {Count} expired sessions")]
    private partial void Removed(int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not remove expired sessions; trying again at the next cleanup")]
    private partial void Failed(Exception error);
}
