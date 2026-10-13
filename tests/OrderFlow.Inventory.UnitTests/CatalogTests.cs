using OrderFlow.Inventory.Domain;

namespace OrderFlow.Inventory.UnitTests;

public class CatalogTests
{
    private static Catalog NewCatalog() => new(
    [
        new CatalogItem("TEST-A", "Test item A", 18.50m, 10),
        new CatalogItem("TEST-B", "Test item B", 6.25m, 100),
    ]);

    [Fact]
    public void Reserving_holds_every_line_and_prices_the_order()
    {
        var catalog = NewCatalog();

        var result = catalog.TryReserve([new RequestedLine("TEST-A", 2), new RequestedLine("TEST-B", 4)]);

        var reserved = result.ShouldBeOfType<ReservationResult.Reserved>();
        reserved.Lines.ShouldBe([new ReservedLine("TEST-A", 2, 18.50m), new ReservedLine("TEST-B", 4, 6.25m)]);
        reserved.Total.ShouldBe(62.00m);
        catalog.Get("TEST-A")!.Available.ShouldBe(8);
        catalog.Get("TEST-B")!.Available.ShouldBe(96);
    }

    [Fact]
    public void Nothing_is_held_when_one_line_is_short()
    {
        var catalog = NewCatalog();

        var result = catalog.TryReserve([new RequestedLine("TEST-B", 1), new RequestedLine("TEST-A", 11)]);

        result.ShouldBeOfType<ReservationResult.Rejected>();
        catalog.Get("TEST-A")!.Available.ShouldBe(10);
        catalog.Get("TEST-B")!.Available.ShouldBe(100);
    }

    [Fact]
    public void Nothing_is_held_when_a_sku_is_unknown()
    {
        var catalog = NewCatalog();

        var result = catalog.TryReserve([new RequestedLine("TEST-A", 1), new RequestedLine("NOPE", 1)]);

        result.ShouldBeOfType<ReservationResult.Rejected>().Reason.ShouldContain("NOPE");
        catalog.Get("TEST-A")!.Available.ShouldBe(10);
    }

    [Fact]
    public void Lines_for_the_same_sku_are_merged_before_checking_stock()
    {
        var catalog = NewCatalog();

        // 6 + 6 = 12 > 10: must be rejected even though each line alone fits.
        var result = catalog.TryReserve([new RequestedLine("TEST-A", 6), new RequestedLine("TEST-A", 6)]);

        result.ShouldBeOfType<ReservationResult.Rejected>();
        catalog.Get("TEST-A")!.Available.ShouldBe(10);
    }

    [Fact]
    public async Task Parallel_reservations_never_oversell()
    {
        var catalog = new Catalog([new CatalogItem("HOT", "Popular item", 1m, 50)]);

        var results = await Task.WhenAll(Enumerable.Range(0, 200)
            .Select(_ => Task.Run(() => catalog.TryReserve([new RequestedLine("HOT", 1)]))));

        results.Count(r => r is ReservationResult.Reserved).ShouldBe(50);
        catalog.Get("HOT")!.Available.ShouldBe(0);
    }

    [Fact]
    public void Two_orders_for_the_same_item_are_both_held_when_stock_allows()
    {
        var catalog = NewCatalog();

        catalog.TryReserve([new RequestedLine("TEST-A", 3)]).ShouldBeOfType<ReservationResult.Reserved>();
        catalog.TryReserve([new RequestedLine("TEST-A", 4)]).ShouldBeOfType<ReservationResult.Reserved>();

        catalog.Get("TEST-A")!.Available.ShouldBe(3);
    }
}
