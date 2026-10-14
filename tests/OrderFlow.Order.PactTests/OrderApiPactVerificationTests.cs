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

    [Fact]
    public void Renaming_a_field_the_ui_reads_fails_verification()
    {
        // Spec 008, AC-5, automated by spec 009 (it was a manual mutation once): the same
        // contract against an API that writes "state" instead of "status".
        var log = new RecordingOutput(output);
        using var host = new PactProviderHost(renameStatusTo: "state");
        host.StartServer();

        using var verifier = new PactVerifier("orderflow-order-api", new PactVerifierConfig
        {
            Outputters = [log],
            LogLevel = PactLogLevel.Information,
        });

        Assert.ThrowsAny<Exception>(() => verifier
            .WithHttpEndpoint(host.BaseUri)
            .WithFileSource(new FileInfo(PactFile()))
            .WithProviderStateUrl(new Uri(host.BaseUri, "/provider-states"))
            .Verify());
        Assert.Contains("missing the following keys: status", log.Text);
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

    private sealed class RecordingOutput(ITestOutputHelper output) : IOutput
    {
        private readonly System.Text.StringBuilder _text = new();

        public string Text => _text.ToString();

        public void WriteLine(string line)
        {
            _text.AppendLine(line);
            output.WriteLine(line);
        }
    }
}
