using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace OrderFlow.Gateway.Sessions;

/// <summary>
/// Sessions in memory: component tests and plain <c>dotnet run</c> (one copy, lost on restart). Tickets
/// are kept serialized, like <see cref="PostgresTicketStore"/>, so a change to a retrieved ticket is lost
/// unless it is stored again: the component tests then catch a renewal that is not saved.
/// </summary>
public sealed class InMemoryTicketStore(TimeProvider clock) : ITicketStore, IExpiredSessions
{
    private readonly ConcurrentDictionary<string, byte[]> _tickets = new();

    /// <summary>How many sessions are held, expired ones included until the cleanup removes them.</summary>
    public int Count => _tickets.Count;

    public Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var key = Guid.NewGuid().ToString("N");
        _tickets[key] = TicketSerializer.Default.Serialize(ticket);
        return Task.FromResult(key);
    }

    public Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        _tickets[key] = TicketSerializer.Default.Serialize(ticket);
        return Task.CompletedTask;
    }

    public Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        AuthenticationTicket? ticket = _tickets.TryGetValue(key, out var bytes) ? TicketSerializer.Default.Deserialize(bytes) : null;
        return Task.FromResult(ticket is not null && !(ticket.Properties.ExpiresUtc < clock.GetUtcNow()) ? ticket : null);
    }

    public Task<int> RemoveExpiredAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var removed = 0;
        foreach (var (key, bytes) in _tickets)
        {
            if (TicketSerializer.Default.Deserialize(bytes)?.Properties.ExpiresUtc <= now && _tickets.TryRemove(new(key, bytes)))
            {
                removed++;
            }
        }

        return Task.FromResult(removed);
    }

    public Task RemoveAsync(string key)
    {
        _tickets.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
