using MassTransit;
using MassTransit.Testing;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using OrderFlow.Testing;

namespace OrderFlow.OrderService.ComponentTests.Support;

/// <summary>
/// Hosts the real Order service in memory, with RabbitMQ swapped for the
/// MassTransit in-memory test harness. One instance per scenario (Reqnroll DI).
/// </summary>
public sealed class OrderServiceFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        // A clock the scenarios move (spec 022: the cancellation window).
        builder.ConfigureTestServices(services => services.AddMassTransitTestHarness().TrustTestTokens().AddSingleton<TimeProvider>(Clock));
    }

    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);

    public ITestHarness Harness => Services.GetTestHarness();

    /// <summary>An API client logged in as the customer (spec 012).</summary>
    public HttpClient ClientFor(string customerId) => CreateClient().LoggedInAs(customerId);

    /// <summary>An API client logged in as a member of staff (spec 015).</summary>
    public HttpClient StaffClient(string userId) => CreateClient().WithToken(TestTokens.Staff(userId));
}
