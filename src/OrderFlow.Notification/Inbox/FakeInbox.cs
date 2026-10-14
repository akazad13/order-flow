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
/// </summary>
public interface IFakeInbox
{
    void Deliver(InboxMessage message);

    /// <summary>
    /// Ordered by when things happened, not when the events arrived: events reach this
    /// service on separate queues, so a confirmation can overtake its order (spec 001).
    /// </summary>
    IReadOnlyList<InboxMessage> For(string customerId);
}

public sealed class InMemoryFakeInbox : IFakeInbox
{
    // One message per (order, text): a repeated event tells the customer nothing new (spec 004, AC-5).
    private readonly ConcurrentDictionary<(Guid OrderId, string Text), InboxMessage> _messages = new();

    public void Deliver(InboxMessage message) => _messages.TryAdd((message.OrderId, message.Text), message);

    public IReadOnlyList<InboxMessage> For(string customerId) => _messages.Values
        .Where(m => m.CustomerId == customerId)
        .OrderBy(m => m.OccurredAt)
        .ThenBy(m => m.ReceivedAt)
        .ToList();
}
