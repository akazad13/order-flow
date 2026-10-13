using System.Collections.Concurrent;
using OrderFlow.OrderService.Domain;

namespace OrderFlow.OrderService.Persistence;

/// <summary>Storage for unit and component tests and plain <c>dotnet run</c> (ADR 0005).</summary>
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

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
