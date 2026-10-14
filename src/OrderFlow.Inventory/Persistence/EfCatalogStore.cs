using Microsoft.EntityFrameworkCore;
using OrderFlow.Inventory.Domain;

namespace OrderFlow.Inventory.Persistence;

/// <summary>
/// PostgreSQL storage. SKUs are stored upper-case, so lookups are case-insensitive like the
/// in-memory catalog. Concurrent reservations of the same item conflict on <c>xmin</c>; the
/// message is retried with a fresh scope (ADR 0005), so stock is never oversold (spec 007, AC-4).
/// </summary>
public sealed class EfCatalogStore(InventoryDbContext db) : ICatalogStore
{
    public async Task<IReadOnlyList<CatalogItem>> ListAsync(CancellationToken cancellationToken) =>
        await db.StockItems.AsNoTracking().OrderBy(i => i.Sku).Select(i => new CatalogItem(i.Sku, i.Name, i.UnitPrice, i.Available)).ToListAsync(cancellationToken);

    public async Task<CatalogItem?> GetAsync(string sku, CancellationToken cancellationToken) =>
        (await db.StockItems.AsNoTracking().FirstOrDefaultAsync(i => i.Sku == Normalize(sku), cancellationToken))?.ToItem();

    public async Task<CatalogItem> SetAsync(string sku, string name, decimal unitPrice, int available, CancellationToken cancellationToken)
    {
        var row = await db.StockItems.FindAsync([Normalize(sku)], cancellationToken);
        if (row is null)
        {
            row = new StockItemRow { Sku = Normalize(sku), Name = name };
            db.StockItems.Add(row);
        }

        row.Name = name;
        row.UnitPrice = unitPrice;
        row.Available = available;
        await db.SaveChangesAsync(cancellationToken);
        return row.ToItem();
    }

    public async Task<ReservationResult> TryReserveAsync(Guid orderId, IReadOnlyList<RequestedLine> lines, CancellationToken cancellationToken)
    {
        // Two copies at once both get past here; the second then fails on xmin or the primary
        // key, is retried with a fresh scope, and stops here (spec 004, AC-6).
        if (await db.Reservations.AnyAsync(r => r.OrderId == orderId, cancellationToken))
        {
            return new ReservationResult.AlreadyDecided();
        }

        var merged = StockRules.Merge(lines.Select(l => l with { Sku = Normalize(l.Sku) }));
        var skus = merged.Select(l => l.Sku).ToList();
        var rows = await db.StockItems.Where(i => skus.Contains(i.Sku)).ToDictionaryAsync(i => i.Sku, cancellationToken);

        var result = StockRules.Check(merged, sku => rows.GetValueOrDefault(sku)?.ToItem());
        if (result is ReservationResult.Reserved reserved)
        {
            foreach (var line in reserved.Lines)
            {
                rows[line.Sku].Available -= line.Quantity;
            }

            db.Reservations.Add(new ReservationRow { OrderId = orderId, Status = StockDecision.Held, Lines = reserved.Lines.ToList() });
        }
        else
        {
            db.Reservations.Add(new ReservationRow { OrderId = orderId, Status = StockDecision.Rejected, Lines = [] });
        }

        return result;
    }

    public async Task<IReadOnlyList<ReservedLine>> ReleaseAsync(Guid orderId, CancellationToken cancellationToken)
    {
        var reservation = await db.Reservations.FindAsync([orderId], cancellationToken);
        if (reservation is not { Status: StockDecision.Held })
        {
            return [];
        }

        var skus = reservation.Lines.Select(l => l.Sku).ToList();
        var rows = await db.StockItems.Where(i => skus.Contains(i.Sku)).ToDictionaryAsync(i => i.Sku, cancellationToken);
        foreach (var line in reservation.Lines)
        {
            if (rows.TryGetValue(line.Sku, out var row))
            {
                row.Available += line.Quantity;
            }
        }

        reservation.Status = StockDecision.Released;
        return reservation.Lines;
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    private static string Normalize(string sku) => sku.ToUpperInvariant();
}
