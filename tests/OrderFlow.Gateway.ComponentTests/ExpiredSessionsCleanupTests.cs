using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OrderFlow.Gateway.Sessions;
using Shouldly;
using Xunit;

namespace OrderFlow.Gateway.ComponentTests;

/// <summary>Spec 024, AC-3: a cleanup that fails is logged, and the next one runs on time.</summary>
public sealed class ExpiredSessionsCleanupTests
{
    [Fact]
    public async Task A_failed_cleanup_is_logged_and_the_next_one_runs_on_time()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));
        var store = new FlakyStore();
        var log = new ListLogger();
        using var cleanup = new ExpiredSessionsCleanup(store, Options.Create(new GatewayOptions()), clock, log);
        await cleanup.StartAsync(CancellationToken.None);

        var first = cleanup.NextCleanup;
        clock.Advance(TimeSpan.FromHours(1));
        await Should.ThrowAsync<IOException>(() => first.WaitAsync(TimeSpan.FromSeconds(10)));
        log.Warnings.ShouldBe(["Could not remove expired sessions; trying again at the next cleanup"]);

        var second = cleanup.NextCleanup;
        clock.Advance(TimeSpan.FromHours(1));
        (await second.WaitAsync(TimeSpan.FromSeconds(10))).ShouldBe(2);
        store.Calls.ShouldBe([clock.GetUtcNow().AddHours(-1), clock.GetUtcNow()]);

        await cleanup.StopAsync(CancellationToken.None);
    }

    /// <summary>The database is unreachable the first time (an I/O error), then removes two sessions.</summary>
    private sealed class FlakyStore : IExpiredSessions
    {
        public List<DateTimeOffset> Calls { get; } = [];

        public Task<int> RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
        {
            Calls.Add(now);
            return Calls.Count == 1 ? Task.FromException<int>(new IOException("database unreachable")) : Task.FromResult(2);
        }
    }

    private sealed class ListLogger : ILogger<ExpiredSessionsCleanup>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
            {
                Warnings.Add(formatter(state, exception));
            }
        }
    }
}
