using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OrderFlow.OrderService.Persistence;

namespace OrderFlow.OrderService.StateMachine;

/// <summary>
/// RabbitMQ only orders messages per publisher. StockReserved (from Inventory) and
/// PaymentSucceeded (from Payment) have different publishers, so PaymentSucceeded can
/// reach this queue first even though Payment only acts after StockReserved.
/// Found by the E2E stability run (about 1 failure in 40, then 7 in 100 with a
/// partitioner, which keeps arrival order and so cannot help).
///
/// An event that arrives before the state machine can handle it (wrong state, or no
/// instance yet: week 4 stress run) is retried with a short back-off; meanwhile the earlier
/// event is processed. Modelling each ordering explicitly
/// (and idempotency) is spec 004.
/// </summary>
public class OrderStateDefinition : SagaDefinition<OrderState>
{
    protected override void ConfigureSaga(
        IReceiveEndpointConfigurator endpointConfigurator,
        ISagaConfigurator<OrderState> sagaConfigurator,
        IRegistrationContext context)
    {
        endpointConfigurator.UseMessageRetry(retry =>
        {
            retry.Handle<UnhandledEventException>();
            retry.Handle<SagaException>();

            // Postgres (spec 007): two first events for one order racing to insert its instance,
            // and two events for one order updating its row at once under REPEATABLE READ.
            retry.Handle<DbUpdateException>();
            retry.Handle<Exception>(IsTransientDatabaseFailure);
            retry.Intervals(50, 100, 250, 500, 1000, 2000);
        });
    }

    /// <summary>PostgreSQL serialization failure (40001) or deadlock (40P01), however deeply wrapped.</summary>
    internal static bool IsTransientDatabaseFailure(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Postgres mode (ADR 0005): retry first, then the consumer outbox, so each retry gets a fresh
/// scope and the state change, order change and published messages commit together.
/// </summary>
public sealed class PostgresOrderStateDefinition : OrderStateDefinition
{
    protected override void ConfigureSaga(
        IReceiveEndpointConfigurator endpointConfigurator,
        ISagaConfigurator<OrderState> sagaConfigurator,
        IRegistrationContext context)
    {
        base.ConfigureSaga(endpointConfigurator, sagaConfigurator, context);
        endpointConfigurator.UseEntityFrameworkOutbox<OrderDbContext>(context);
    }
}
