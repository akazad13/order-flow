using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace OrderFlow.Inventory.ComponentTests.Support;

/// <summary>
/// Hosts the real Inventory service in memory, with RabbitMQ swapped for the
/// MassTransit in-memory test harness. One instance per scenario (Reqnroll DI).
/// </summary>
public sealed class InventoryServiceFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.UseSetting(Api.StockEndpoints.EnableTestEndpointsKey, "true");
        builder.ConfigureTestServices(services => services.AddMassTransitTestHarness());
    }

    public ITestHarness Harness => Services.GetTestHarness();
}
