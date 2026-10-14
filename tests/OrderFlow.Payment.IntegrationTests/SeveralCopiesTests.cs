using System.Net.Http.Json;
using OrderFlow.Contracts.Events;
using OrderFlow.IntegrationTesting;
using OrderFlow.Payment.Persistence;

namespace OrderFlow.Payment.IntegrationTests;

/// <summary>Spec 013: copies of the Payment service on one database and one broker. The test bus plays Inventory.</summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SeveralCopiesTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task A_switch_set_through_one_copy_applies_on_another()
    {
        // AC-2: the switches are set through one copy, which then stops, so the other copy is the
        // one that takes the payments.
        var settings = await SettingsAsync();
        await using var testBus = await TestBus.StartAsync(infrastructure, settings["RabbitMq:VirtualHost"]!);
        await using var other = new ServiceHost<Program>(settings);
        await other.StartAsync();

        var declined = StockReservedFor($"declined-{Guid.NewGuid():N}");
        var stalled = StockReservedFor($"stalled-{Guid.NewGuid():N}");
        await using (var first = new ServiceHost<Program>(settings))
        {
            var client = await first.StartAsync();
            (await client.PutAsJsonAsync($"/test/declines/{declined.CustomerId}", new DeclineRule("Card declined"))).EnsureSuccessStatusCode();
            (await client.PutAsync($"/test/stalls/{stalled.CustomerId}", null)).EnsureSuccessStatusCode();
        }

        var handled = HandledCounter<StockReserved>.On(other.Services, m => m.OrderId == stalled.OrderId);
        await testBus.Publish(declined);
        await testBus.Publish(stalled);

        (await testBus.WaitFor<PaymentFailed>(m => m.OrderId == declined.OrderId)).Reason.ShouldBe("Card declined");
        await handled.WaitFor(1);

        // Anything announced for the stalled order would have arrived before this later order's charge.
        var probe = StockReservedFor("customer-1");
        await testBus.Publish(probe);
        await testBus.WaitFor<PaymentSucceeded>(m => m.OrderId == probe.OrderId);
        testBus.Received<PaymentSucceeded>().ShouldNotContain(m => m.OrderId == declined.OrderId || m.OrderId == stalled.OrderId);
        testBus.Received<PaymentFailed>().ShouldNotContain(m => m.OrderId == stalled.OrderId);
    }

    [Fact]
    public async Task Three_copies_start_together_on_a_new_database()
    {
        // AC-3
        await StartTogether.ThreeCopiesMigrateOnceAsync<Program, PaymentDbContext>(await SettingsAsync());
    }

    private static StockReserved StockReservedFor(string customerId) =>
        new(Guid.NewGuid(), customerId, [new StockReservedLine("ITEM-1", 1, 10.00m)], 10.00m, DateTimeOffset.UtcNow);

    private async Task<Dictionary<string, string?>> SettingsAsync()
    {
        var settings = infrastructure.RabbitSettings(await infrastructure.CreateVirtualHostAsync());
        settings["Persistence:Provider"] = "Postgres";
        settings["ConnectionStrings:Payments"] = infrastructure.ConnectionString($"payments_{Guid.NewGuid():N}");
        settings[TestEndpoints.EnableKey] = "true";
        return settings;
    }
}
