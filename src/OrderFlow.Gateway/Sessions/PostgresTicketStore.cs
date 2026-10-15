using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Gateway.Persistence;

namespace OrderFlow.Gateway.Sessions;

/// <summary>
/// Sessions in PostgreSQL (spec 023, AC-6): the cookie holds only a key; the ticket, with the tokens,
/// stays here, so any copy of the gateway serves any session. The ticket is protected like the cookie
/// would be (the data protection keys are shared through the same database).
/// </summary>
public sealed class PostgresTicketStore(IServiceScopeFactory scopes, TimeProvider clock) : ITicketStore
{
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString("N");
        await WithDb(async db =>
        {
            db.Sessions.Add(new SessionRow { Key = key, Ticket = Serialize(ticket), ExpiresAt = ticket.Properties.ExpiresUtc });
            await db.SaveChangesAsync();
        });
        return key;
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket) => WithDb(async db =>
    {
        var row = await db.Sessions.FindAsync(key);
        if (row is null)
        {
            db.Sessions.Add(new SessionRow { Key = key, Ticket = Serialize(ticket), ExpiresAt = ticket.Properties.ExpiresUtc });
        }
        else
        {
            row.Ticket = Serialize(ticket);
            row.ExpiresAt = ticket.Properties.ExpiresUtc;
        }

        await db.SaveChangesAsync();
    });

    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        AuthenticationTicket? ticket = null;
        await WithDb(async db =>
        {
            var row = await db.Sessions.AsNoTracking().SingleOrDefaultAsync(s => s.Key == key);
            if (row is not null && (row.ExpiresAt is null || row.ExpiresAt > clock.GetUtcNow()))
            {
                ticket = TicketSerializer.Default.Deserialize(row.Ticket);
            }
        });
        return ticket;
    }

    public Task RemoveAsync(string key) => WithDb(db => db.Sessions.Where(s => s.Key == key).ExecuteDeleteAsync());

    private static byte[] Serialize(AuthenticationTicket ticket) => TicketSerializer.Default.Serialize(ticket);

    private async Task WithDb(Func<GatewayDbContext, Task> work)
    {
        await using var scope = scopes.CreateAsyncScope();
        await work(scope.ServiceProvider.GetRequiredService<GatewayDbContext>());
    }
}
