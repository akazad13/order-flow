using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace OrderFlow.Notification.ComponentTests.Support;

/// <summary>
/// Hosts the real Notification service in memory, with RabbitMQ swapped for the
/// MassTransit in-memory test harness. One instance per scenario (Reqnroll DI).
/// </summary>
public sealed class NotificationServiceFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services => services.AddMassTransitTestHarness());
    }

    public ITestHarness Harness => Services.GetTestHarness();
}
