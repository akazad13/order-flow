namespace OrderFlow.Inventory.Domain;

/// <summary>
/// Catalog storage: in memory (<see cref="InMemoryCatalogStore"/>) or PostgreSQL
/// (<c>EfCatalogStore</c>), chosen by <c>Persistence:Provider</c> (ADR 0005).
/// </summary>
public interface ICatalogStore
{
    Task<IReadOnlyList<CatalogItem>> ListAsync(CancellationToken cancellationToken);

    Task<CatalogItem?> GetAsync(string sku, CancellationToken cancellationToken);

    /// <summary>Creates or replaces an item and saves immediately (test endpoint, no messages involved).</summary>
    Task<CatalogItem> SetAsync(string sku, string name, decimal unitPrice, int available, CancellationToken cancellationToken);

    Task<ReservationResult> TryReserveAsync(Guid orderId, IReadOnlyList<RequestedLine> lines, CancellationToken cancellationToken);

    Task<IReadOnlyList<ReservedLine>> ReleaseAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>
    /// Commits reservation changes together with the messages published in this scope
    /// (the consumer outbox, spec 007 R-2). Consumers call it after publishing.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed class InMemoryCatalogStore(Catalog catalog) : ICatalogStore
{
    public Task<IReadOnlyList<CatalogItem>> ListAsync(CancellationToken cancellationToken) => Task.FromResult(catalog.List());

    public Task<CatalogItem?> GetAsync(string sku, CancellationToken cancellationToken) => Task.FromResult(catalog.Get(sku));

    public Task<CatalogItem> SetAsync(string sku, string name, decimal unitPrice, int available, CancellationToken cancellationToken) =>
        Task.FromResult(catalog.Set(sku, name, unitPrice, available));

    public Task<ReservationResult> TryReserveAsync(Guid orderId, IReadOnlyList<RequestedLine> lines, CancellationToken cancellationToken) =>
        Task.FromResult(catalog.TryReserve(orderId, lines));

    public Task<IReadOnlyList<ReservedLine>> ReleaseAsync(Guid orderId, CancellationToken cancellationToken) =>
        Task.FromResult(catalog.Release(orderId));

    public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
