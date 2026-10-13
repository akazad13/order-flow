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
    private readonly ConcurrentDictionary<string, ConcurrentQueue<InboxMessage>> _messages = new();

    public void Deliver(InboxMessage message) =>
        _messages.GetOrAdd(message.CustomerId, _ => new ConcurrentQueue<InboxMessage>()).Enqueue(message);

    public IReadOnlyList<InboxMessage> For(string customerId) =>
        _messages.TryGetValue(customerId, out var queue)
            ? queue.OrderBy(m => m.OccurredAt).ThenBy(m => m.ReceivedAt).ToList()
            : [];
}
