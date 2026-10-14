using Microsoft.EntityFrameworkCore;
using OrderFlow.Notification.Inbox;

namespace OrderFlow.Notification.Persistence;

/// <summary>
/// PostgreSQL storage (spec 011). Two copies of an event racing both find no message and both add
/// one; the second save fails on the (order, text) unique index, and the message is retried with a
/// fresh scope, which finds the message already there.
/// </summary>
public sealed class EfFakeInbox(NotificationDbContext db) : IFakeInbox
{
    public async Task DeliverAsync(InboxMessage message, CancellationToken cancellationToken)
    {
        if (await db.Messages.AnyAsync(m => m.OrderId == message.OrderId && m.Text == message.Text, cancellationToken))
        {
            return;
        }

        db.Messages.Add(new InboxMessageRow
        {
            MessageId = message.MessageId,
            CustomerId = message.CustomerId,
            OrderId = message.OrderId,
            Text = message.Text,
            OccurredAt = message.OccurredAt,
            ReceivedAt = message.ReceivedAt,
        });
    }

    public async Task<IReadOnlyList<InboxMessage>> ForAsync(string customerId, CancellationToken cancellationToken) =>
        (await db.Messages.AsNoTracking()
            .Where(m => m.CustomerId == customerId)
            .OrderBy(m => m.OccurredAt)
            .ThenBy(m => m.ReceivedAt)
            .ToListAsync(cancellationToken))
        .Select(m => m.ToMessage())
        .ToList();

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
