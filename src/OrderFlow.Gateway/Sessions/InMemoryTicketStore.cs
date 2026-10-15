using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace OrderFlow.Gateway.Sessions;

/// <summary>
/// Sessions in memory: component tests and plain <c>dotnet run</c> (one copy, lost on restart). Tickets
/// are kept serialized, like <see cref="PostgresTicketStore"/>, so a change to a retrieved ticket is lost
/// unless it is stored again: the component tests then catch a renewal that is not saved.
/// </summary>
public sealed class InMemoryTicketStore(TimeProvider clock) : ITicketStore
{
    private readonly ConcurrentDictionary<string, byte[]> _tickets = new();

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

    public Task RemoveAsync(string key)
    {
        _tickets.TryRemove(key, out _);
        return Task.CompletedTask;
    }
}
