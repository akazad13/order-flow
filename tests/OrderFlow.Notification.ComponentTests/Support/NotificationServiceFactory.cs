using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Testing;

namespace OrderFlow.Notification.ComponentTests.Support;

/// <summary>
/// Hosts the real Notification service in memory, with RabbitMQ swapped for the
/// MassTransit in-memory test harness. One instance per scenario (Reqnroll DI).
/// </summary>
public sealed class NotificationServiceFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services => services.AddMassTransitTestHarness().TrustTestTokens());
    }

    public ITestHarness Harness => Services.GetTestHarness();

    /// <summary>An API client logged in as the customer (spec 012).</summary>
    public HttpClient ClientFor(string customerId) => CreateClient().LoggedInAs(customerId);
}
