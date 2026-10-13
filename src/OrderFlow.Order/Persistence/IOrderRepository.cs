using OrderFlow.OrderService.Domain;

namespace OrderFlow.OrderService.Persistence;

public interface IOrderRepository
{
    Task AddAsync(Order order, CancellationToken cancellationToken);

    Task<Order?> GetAsync(Guid id, CancellationToken cancellationToken);
}
