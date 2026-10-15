using System.Collections.Concurrent;
using System.Text.Json;
using Json.Schema;
using MassTransit.Serialization;
using OrderFlow.Contracts.Events;

namespace OrderFlow.ContractTests.Events;

/// <summary>
/// Every C# event record and its published JSON Schema must agree. Serializes with
/// the same options MassTransit uses on the wire, then validates against the contract.
/// Adding an event? Add a row to <see cref="Events"/>.
/// </summary>
public class EventSchemaContractTests
{
    private static readonly EvaluationOptions Options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
    };

    public static TheoryData<string, object> Events => new()
    {
        { "order-placed.v1.json", new OrderPlaced(Guid.NewGuid(), "customer-1", [new OrderPlacedLine("SKU-001", 2)], DateTimeOffset.UtcNow) },
        {
            "stock-reserved.v1.json",
            new StockReserved(Guid.NewGuid(), "customer-1", [new StockReservedLine("SKU-001", 2, 18.50m), new StockReservedLine("SKU-003", 4, 6.25m)], 62.00m, DateTimeOffset.UtcNow)
        },
        { "payment-succeeded.v1.json", new PaymentSucceeded(Guid.NewGuid(), Guid.NewGuid(), 62.00m, DateTimeOffset.UtcNow) },
        { "order-confirmed.v1.json", new OrderConfirmed(Guid.NewGuid(), "customer-1", 62.00m, DateTimeOffset.UtcNow) },
        {
            "stock-rejected.v1.json",
            new StockRejected(Guid.NewGuid(), "customer-1", [new StockShortage("SKU-002", 3, 1), new StockShortage("NO-SUCH", 1, 0)], DateTimeOffset.UtcNow)
        },
        { "payment-failed.v1.json", new PaymentFailed(Guid.NewGuid(), "Card declined", DateTimeOffset.UtcNow) },
        {
            "order-cancelled.v2.json",
            new OrderCancelledV2(Guid.NewGuid(), "customer-1", CancellationReasons.StaffRequested, "Cancelled by our support team at the customer's request.", DateTimeOffset.UtcNow)
        },
        {
            "order-cancelled.v2.json",
            new OrderCancelledV2(Guid.NewGuid(), "customer-1", CancellationReasons.OutOfStock, "Only 1 of SKU-002 available, 3 requested.", DateTimeOffset.UtcNow)
        },
        {
            "order-cancelled.v2.json",
            new OrderCancelledV2(Guid.NewGuid(), "customer-1", CancellationReasons.PaymentDeclined, "Card declined", DateTimeOffset.UtcNow)
        },
        {
            "order-cancelled.v2.json",
            new OrderCancelledV2(Guid.NewGuid(), "customer-1", CancellationReasons.CustomerRequested, "Cancelled at the customer's request.", DateTimeOffset.UtcNow)
        },
        { "stock-released.v1.json", new StockReleased(Guid.NewGuid(), [new StockReleasedLine("SKU-001", 3)], DateTimeOffset.UtcNow) },
        { "order-cancellation-requested.v1.json", new OrderCancellationRequested(Guid.NewGuid(), DateTimeOffset.UtcNow) },
        { "order-cancellation-requested.v1.json", new OrderCancellationRequested(Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationRequesters.Customer) },
        { "order-cancellation-requested.v1.json", new OrderCancellationRequested(Guid.NewGuid(), DateTimeOffset.UtcNow, CancellationRequesters.Staff) },
        { "payment-refunded.v1.json", new PaymentRefunded(Guid.NewGuid(), "customer-1", Guid.NewGuid(), 37.00m, DateTimeOffset.UtcNow) },
    };

    [Theory]
    [MemberData(nameof(Events))]
    public void Published_message_matches_its_schema(string schemaFile, object message)
    {
        var json = JsonSerializer.SerializeToElement(message, message.GetType(), SystemTextJsonMessageSerializer.Options);

        var result = LoadSchema(schemaFile).Evaluate(json, Options);

        result.IsValid.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Every_schema_file_has_a_test()
    {
        var tested = Events.Select(row => (string)row[0]).ToHashSet();
        var files = Directory.GetFiles(RepoPath.Combine("contracts", "events", "schemas"), "*.json").Select(Path.GetFileName);

        files.ShouldAllBe(f => tested.Contains(f!));
    }

    [Fact]
    public void Schema_rejects_a_message_with_an_unknown_field()
    {
        // Guards against schemas so loose that the tests above prove nothing.
        var json = JsonDocument.Parse("""
            {
              "orderId": "8f6c1d1e-6f0b-4c64-9d7e-0a8f6b1b2c3d",
              "customerId": "customer-1",
              "lines": [{ "sku": "SKU-001", "quantity": 2 }],
              "placedAt": "2026-10-14T10:00:00Z",
              "unexpected": true
            }
            """).RootElement;

        LoadSchema("order-placed.v1.json").Evaluate(json, Options).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Schema_rejects_an_amount_with_more_than_two_decimals()
    {
        var json = JsonDocument.Parse("""
            {
              "orderId": "8f6c1d1e-6f0b-4c64-9d7e-0a8f6b1b2c3d",
              "paymentId": "1b2c3d4e-6f0b-4c64-9d7e-0a8f6b1b2c3d",
              "amount": "10.005",
              "paidAt": "2026-10-14T10:00:00Z"
            }
            """).RootElement;

        LoadSchema("payment-succeeded.v1.json").Evaluate(json, Options).IsValid.ShouldBeFalse();
    }

    // The schema library registers each $id globally and refuses to load one twice.
    private static readonly ConcurrentDictionary<string, JsonSchema> Schemas = new();

    private static JsonSchema LoadSchema(string file) =>
        Schemas.GetOrAdd(file, f => JsonSchema.FromFile(RepoPath.Combine("contracts", "events", "schemas", f)));

    private static string Describe(EvaluationResults result) =>
        JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
}
