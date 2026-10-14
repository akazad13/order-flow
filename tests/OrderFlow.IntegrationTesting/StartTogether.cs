using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace OrderFlow.IntegrationTesting;

/// <summary>Spec 013, AC-3: copies of a service starting at the same moment on a database that does not exist yet.</summary>
public static class StartTogether
{
    /// <summary>Starts three copies together, then checks every migration was applied; throws if any copy failed to start.</summary>
    public static async Task ThreeCopiesMigrateOnceAsync<TProgram, TContext>(IReadOnlyDictionary<string, string?> settings)
        where TProgram : class
        where TContext : DbContext
    {
        var copies = await ServiceHost<TProgram>.StartTogetherAsync(settings, 3);
        try
        {
            await using var scope = copies[0].Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<TContext>();
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            var expected = db.Database.GetMigrations().ToList();
            if (!applied.SequenceEqual(expected))
            {
                throw new InvalidOperationException($"Applied migrations [{string.Join(", ", applied)}] differ from [{string.Join(", ", expected)}].");
            }
        }
        finally
        {
            foreach (var copy in copies)
            {
                await copy.DisposeAsync();
            }
        }
    }
}
