using PactNet;
using PactNet.Infrastructure.Outputters;
using PactNet.Verifier;
using Xunit.Abstractions;

namespace OrderFlow.OrderService.PactTests;

/// <summary>
/// Provider side of the web UI / Order API contract (spec 008, AC-3): replays every
/// interaction the web tests recorded in pacts/ against the real Order API.
/// </summary>
[Trait("Category", "Contract")]
public sealed class OrderApiPactVerificationTests(ITestOutputHelper output)
{
    [Fact]
    public void Order_api_honours_the_web_ui_contract()
    {
        using var host = new PactProviderHost();
        host.StartServer();

        using var verifier = new PactVerifier("orderflow-order-api", new PactVerifierConfig
        {
            Outputters = [new XunitOutput(output)],
            LogLevel = PactLogLevel.Information,
        });

        verifier
            .WithHttpEndpoint(host.BaseUri)
            .WithFileSource(new FileInfo(PactFile()))
            .WithProviderStateUrl(new Uri(host.BaseUri, "/provider-states"))
            .Verify();
    }

    private static string PactFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "OrderFlow.slnx")))
        {
            dir = dir.Parent;
        }

        return Path.Combine(dir!.FullName, "pacts", "orderflow-web-orderflow-order-api.json");
    }

    private sealed class XunitOutput(ITestOutputHelper output) : IOutput
    {
        public void WriteLine(string line) => output.WriteLine(line);
    }
}
