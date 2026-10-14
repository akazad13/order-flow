using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Testing;

namespace OrderFlow.Inventory.ComponentTests.Support;

/// <summary>
/// Hosts the real Inventory service in memory, with RabbitMQ swapped for the
/// MassTransit in-memory test harness. One instance per scenario (Reqnroll DI).
/// </summary>
public sealed class InventoryServiceFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        // Logins signed by the test key (spec 012); setting stock needs an admin (spec 017).
        builder.ConfigureTestServices(services => services.AddMassTransitTestHarness().TrustTestTokens());
    }

    public ITestHarness Harness => Services.GetTestHarness();

    /// <summary>An API client logged in as an admin (spec 017).</summary>
    public HttpClient AdminClient(string userId = "admin-1") => CreateClient().WithToken(TestTokens.Admin(userId));
}
