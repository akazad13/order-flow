using System.Net;
using System.Net.Http.Json;
using OrderFlow.Gateway.ComponentTests.Support;
using Reqnroll;
using Shouldly;

namespace OrderFlow.Gateway.ComponentTests.Steps;

/// <summary>
/// Shared state and helpers used by both gateway feature files (spec 023, spec 024).
/// One instance per scenario (Reqnroll's default DI scope).
/// </summary>
[Binding]
public sealed class GatewayContext(GatewayFactory factory) : IAsyncDisposable
{
    private HttpClient? _browser;

    /// <summary>
    /// The browser client — created lazily so that settings like <see cref="GatewayFactory.SecureCookie"/>
    /// can be configured before the first request (which triggers the app to initialise).
    /// </summary>
    public HttpClient Browser => _browser ??= factory.Browser();

    /// <summary>The last HTTP response the scenario received.</summary>
    public HttpResponseMessage? LastResponse { get; set; }

    /// <summary>
    /// Signs in as <paramref name="user"/> via the test-only endpoint, so the browser cookie is set.
    /// </summary>
    public async Task SignInAsync(string user, int expiresInSeconds, string roles = "")
    {
        var url = $"/test/sign-in?user={Uri.EscapeDataString(user)}&expiresIn={expiresInSeconds}&roles={Uri.EscapeDataString(roles)}";
        var response = await Browser.PostAsync(url, content: null);
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        LastResponse?.Dispose();
        _browser?.Dispose();
    }
}

