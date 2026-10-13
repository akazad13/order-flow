using OrderFlow.OrderService.Domain;

namespace OrderFlow.OrderService.Persistence;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken);

    Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Commits the changes and any messages published in this scope (the bus outbox).
    /// The state machine's changes are committed by the saga repository instead.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
