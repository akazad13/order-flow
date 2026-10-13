using OrderFlow.OrderService.Domain;

namespace OrderFlow.OrderService.Persistence;

public sealed class EfOrderRepository(OrderDbContext db) : IOrderRepository
{
    public Task AddAsync(Order order, CancellationToken cancellationToken)
    {
        db.Orders.Add(order);
        return Task.CompletedTask;
    }

    public async Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Orders.FindAsync([id], cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}
