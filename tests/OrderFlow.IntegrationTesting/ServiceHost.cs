using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using OrderFlow.Testing;

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

        // Logins signed by the test key (spec 012, ADR 0008); harmless for services without logins.
        builder.ConfigureTestServices(services => services.TrustTestTokens());
    }

    /// <summary>
    /// Starts several copies of the service with the same settings at the same moment (spec 013,
    /// AC-3). Throws if any copy fails to start; the caller disposes the copies.
    /// </summary>
    public static async Task<ServiceHost<TProgram>[]> StartTogetherAsync(IReadOnlyDictionary<string, string?> settings, int count)
    {
        var copies = Enumerable.Range(0, count).Select(_ => new ServiceHost<TProgram>(settings)).ToArray();
        using var go = new ManualResetEventSlim();
        var starting = copies.Select(copy => Task.Run(() =>
        {
            go.Wait();
            return copy.StartAsync();
        })).ToArray();

        go.Set();
        await Task.WhenAll(starting);
        return copies;
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
