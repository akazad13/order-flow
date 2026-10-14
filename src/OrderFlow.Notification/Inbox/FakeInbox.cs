using System.Collections.Concurrent;

namespace OrderFlow.Notification.Inbox;

public sealed record InboxMessage(
    Guid MessageId,
    string CustomerId,
    Guid OrderId,
    string Text,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt);

/// <summary>
/// Stands in for email/SMS so tests can assert what the customer was told.
/// Exposed over HTTP at GET /inbox/{customerId} (contracts/http/notification.openapi.yaml).
/// In memory (<see cref="InMemoryFakeInbox"/>) or PostgreSQL (<c>EfFakeInbox</c>), chosen by
/// <c>Persistence:Provider</c> (ADR 0005, spec 011).
/// </summary>
public interface IFakeInbox
{
    /// <summary>
    /// Adds the message unless the customer was already told the same thing about the same order
    /// (spec 004, AC-5; spec 011, R-1). Takes effect on <see cref="SaveChangesAsync"/>.
    /// </summary>
    Task DeliverAsync(InboxMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Ordered by when things happened, not when the events arrived: events reach this
    /// service on separate queues, so a confirmation can overtake its order (spec 001).
    /// </summary>
    Task<IReadOnlyList<InboxMessage>> ForAsync(string customerId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>The in-memory storage mode (ADR 0005): component tests and plain <c>dotnet run</c>. A restart empties it.</summary>
public sealed class InMemoryFakeInbox : IFakeInbox
{
    // One message per (order, text): a repeated event tells the customer nothing new (spec 004, AC-5).
    private readonly ConcurrentDictionary<(Guid OrderId, string Text), InboxMessage> _messages = new();

    public Task DeliverAsync(InboxMessage message, CancellationToken cancellationToken)
    {
        _messages.TryAdd((message.OrderId, message.Text), message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<InboxMessage>> ForAsync(string customerId, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<InboxMessage>>(_messages.Values
            .Where(m => m.CustomerId == customerId)
            .OrderBy(m => m.OccurredAt)
            .ThenBy(m => m.ReceivedAt)
            .ToList());

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
