using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OrderFlow.IntegrationTesting;

/// <summary>
/// Runs a real service in-process against the test containers. Dispose it and create a new
/// one with the same settings to simulate a restart.
/// </summary>
public sealed class ServiceHost<TProgram>(IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<TProgram>
    where TProgram : class
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }

    /// <summary>Starts the host and waits until its health check (including the bus) is healthy.</summary>
    public async Task<HttpClient> StartAsync(TimeSpan? timeout = null)
    {
        var client = CreateClient();
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        while (true)
        {
            var response = await client.GetAsync("/health");
            if (response.IsSuccessStatusCode)
            {
                return client;
            }

            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"Service not healthy: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
            }

            await Task.Delay(100);
        }
    }
}
