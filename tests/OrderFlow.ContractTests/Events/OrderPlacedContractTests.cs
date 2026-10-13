using System.Text.Json;
using Json.Schema;
using MassTransit.Serialization;
using OrderFlow.Contracts.Events;

namespace OrderFlow.ContractTests.Events;

/// <summary>
/// The C# record and the published JSON Schema must agree. Serializes with the
/// same options MassTransit uses on the wire, then validates against the contract.
/// </summary>
public class OrderPlacedContractTests
{
    private static readonly JsonSchema Schema = JsonSchema.FromFile(RepoPath.Combine("contracts", "events", "schemas", "order-placed.v1.json"));

    private static readonly EvaluationOptions Options = new()
    {
        OutputFormat = OutputFormat.List,
        RequireFormatValidation = true,
    };

    [Fact]
    public void Published_message_matches_the_v1_schema()
    {
        var message = new OrderPlaced(Guid.NewGuid(), "customer-1", [new OrderPlacedLine("SKU-001", 2)], DateTimeOffset.UtcNow);

        var json = JsonSerializer.SerializeToElement(message, SystemTextJsonMessageSerializer.Options);
        var result = Schema.Evaluate(json, Options);

        result.IsValid.ShouldBeTrue(Describe(result));
    }

    [Fact]
    public void Schema_rejects_a_message_with_an_unknown_field()
    {
        // Guards against the schema being so loose that the test above proves nothing.
        var json = JsonDocument.Parse("""
            {
              "orderId": "8f6c1d1e-6f0b-4c64-9d7e-0a8f6b1b2c3d",
              "customerId": "customer-1",
              "lines": [{ "sku": "SKU-001", "quantity": 2 }],
              "placedAt": "2026-10-14T10:00:00Z",
              "unexpected": true
            }
            """).RootElement;

        Schema.Evaluate(json, Options).IsValid.ShouldBeFalse();
    }

    private static string Describe(EvaluationResults result) =>
        JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
}
