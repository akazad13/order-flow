using OrderFlow.Inventory.Domain;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Inventory.Api;

// Shapes match contracts/http/inventory.openapi.yaml.
public sealed record SetStockRequest(string? Name, decimal UnitPrice, int Available);

public static class StockEndpoints
{
    public static IEndpointRouteBuilder MapStockEndpoints(this IEndpointRouteBuilder app)
    {
        // Looking at the catalog is open to everyone (spec 017, AC-3).
        app.MapGet("/stock", async (ICatalogStore catalog, CancellationToken ct) => Results.Ok(await catalog.ListAsync(ct)))
            .WithName("listStock");

        app.MapGet("/stock/{sku}", async (string sku, ICatalogStore catalog, CancellationToken ct) =>
            await catalog.GetAsync(sku, ct) is { } item ? Results.Ok(item) : Results.NotFound()).WithName("getStock");

        // Admins set an item's name, price and quantity available (spec 017): no login 401, not an admin 403.
        // Until spec 017 this was a test-only endpoint behind Inventory:EnableTestEndpoints (spec 001, Q4).
        app.MapPut("/stock/{sku}", SetStock)
            .WithName("setStock")
            .RequireAuthorization(policy => policy.RequireRole(AuthenticationExtensions.AdminRole));

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
