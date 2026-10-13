using OrderFlow.Inventory.Domain;

namespace OrderFlow.Inventory.Api;

// Shapes match contracts/http/inventory.openapi.yaml.
public sealed record SetStockRequest(string? Name, decimal UnitPrice, int Available);

public static class StockEndpoints
{
    public const string EnableTestEndpointsKey = "Inventory:EnableTestEndpoints";

    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder app, IConfiguration configuration)
    {
        app.MapGet("/stock", async (ICatalogStore catalog, CancellationToken ct) => Results.Ok(await catalog.ListAsync(ct)))
            .WithName("listStock");

        app.MapGet("/stock/{sku}", async (string sku, ICatalogStore catalog, CancellationToken ct) =>
            await catalog.GetAsync(sku, ct) is { } item ? Results.Ok(item) : Results.NotFound()).WithName("getStock");

        // Test-only: lets tests and demos set exact stock levels (spec 001, Q4).
        if (configuration.GetValue<bool>(EnableTestEndpointsKey))
        {
            app.MapPut("/stock/{sku}", SetStock).WithName("setStock");
        }

        return app;
    }

    private static async Task<IResult> SetStock(string sku, SetStockRequest request, ICatalogStore catalog, CancellationToken ct)
    {
        var errors = new Dictionary<string, string[]>();
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            errors["name"] = ["Name is required."];
        }

        if (request.UnitPrice < 0)
        {
            errors["unitPrice"] = ["Unit price cannot be negative."];
        }

        if (request.Available < 0)
        {
            errors["available"] = ["Available cannot be negative."];
        }

        return errors.Count > 0
            ? Results.ValidationProblem(errors)
            : Results.Ok(await catalog.SetAsync(sku, request.Name!, request.UnitPrice, request.Available, ct));
    }
}
