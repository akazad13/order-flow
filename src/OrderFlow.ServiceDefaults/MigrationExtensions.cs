using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace OrderFlow.ServiceDefaults;

public static class MigrationExtensions
{
    /// <summary>
    /// Migrates the service's database at startup (ADR 0005), then runs <paramref name="seed"/>, one
    /// copy at a time (spec 013, AC-3). EF Core's own migration lock is not enough: copies starting
    /// together on a new database both applied the second migration. The copies take turns through a
    /// PostgreSQL advisory lock named after the database, held on the <c>postgres</c> maintenance
    /// database because the service's own may not exist yet.
    /// </summary>
    public static async Task MigrateAsync<TContext>(this WebApplication app, Func<TContext, Task>? seed = null)
        where TContext : DbContext
    {
        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TContext>();

        var target = new NpgsqlConnectionStringBuilder(db.Database.GetConnectionString());
        var maintenance = new NpgsqlConnectionStringBuilder(target.ConnectionString) { Database = "postgres", Pooling = false };

        // A session lock: closing the (unpooled) connection releases it, even if migrating fails.
        await using var connection = new NpgsqlConnection(maintenance.ConnectionString);
        await connection.OpenAsync();
        await using (var take = new NpgsqlCommand("SELECT pg_advisory_lock(hashtext($1))", connection))
        {
            take.Parameters.AddWithValue($"orderflow-migrate:{target.Database}");
            await take.ExecuteNonQueryAsync();
        }

        await db.Database.MigrateAsync();
        if (seed is not null)
        {
            await seed(db);
        }
    }
}
