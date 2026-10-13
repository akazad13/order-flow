using System.Collections.Concurrent;
using OrderFlow.OrderService.Domain;

namespace OrderFlow.OrderService.Persistence;

/// <summary>Week 1 only. Replaced by PostgreSQL + outbox in milestone 4 (week 5).</summary>
public sealed class InMemoryOrderRepository : IOrderRepository
{
    private readonly ConcurrentDictionary<Guid, Order> _orders = new();

    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        _orders[order.Id] = order;
        return Task.CompletedTask;
    }

    public Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(_orders.GetValueOrDefault(id));
}
