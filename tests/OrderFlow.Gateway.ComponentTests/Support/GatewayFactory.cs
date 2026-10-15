using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using OrderFlow.Gateway.Sessions;
using Yarp.ReverseProxy.Forwarder;

namespace OrderFlow.Gateway.ComponentTests.Support;

/// <summary>
/// The real gateway in memory (spec 023). Keycloak's token endpoint and the services it forwards to
/// are fakes that record what they receive; the clock is one the scenarios move. Sessions start
/// through a test-only sign-in, so the scenarios need no real login (E2E has that).
/// </summary>
public sealed class GatewayFactory : WebApplicationFactory<Program>
{
    public const string Keycloak = "https://keycloak.test/realms/orderflow/protocol/openid-connect";

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 15, 12, 0, 0, TimeSpan.Zero));

    public FakeServices Downstream { get; } = new();

    public FakeKeycloak TokenEndpoint { get; } = new();

    /// <summary>Set before the first request: the production cookie setting (AC-7).</summary>
    public bool SecureCookie { get; set; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("Gateway:ClientSecret", "test-secret");
        builder.UseSetting("Gateway:SecureCookie", SecureCookie ? "true" : "false");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IForwarderHttpClientFactory>(Downstream);
            services.AddHttpClient(TokenRefresher.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => TokenEndpoint);
            services.PostConfigure<OpenIdConnectOptions>(OpenIdConnectDefaults.AuthenticationScheme, options =>
            {
                var configuration = new OpenIdConnectConfiguration
                {
                    Issuer = "https://keycloak.test/realms/orderflow",
                    AuthorizationEndpoint = $"{Keycloak}/auth",
                    TokenEndpoint = $"{Keycloak}/token",
                    EndSessionEndpoint = $"{Keycloak}/logout",
                };
                options.Configuration = configuration;
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            });
            services.AddSingleton<IStartupFilter>(new TestSignIn(Clock));
        });
    }

    /// <summary>The sessions the gateway holds (in memory in these scenarios; spec 024).</summary>
    public InMemoryTicketStore Sessions => (InMemoryTicketStore)Services.GetRequiredService<ITicketStore>();

    /// <summary>The background cleanup of expired sessions (spec 024).</summary>
    public ExpiredSessionsCleanup Cleanup => Services.GetServices<IHostedService>().OfType<ExpiredSessionsCleanup>().Single();

    /// <summary>A browser: it keeps cookies and does not follow redirects.</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true, AllowAutoRedirect = false });

    /// <summary>Test-only: POST /test/sign-in starts a session as a login through Keycloak would.</summary>
    private sealed class TestSignIn(TimeProvider clock) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Map("/test/sign-in", signIn => signIn.Run(async context =>
            {
                var user = context.Request.Query["user"].ToString();
                var roles = context.Request.Query["roles"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries);
                var expiresIn = int.Parse(context.Request.Query["expiresIn"].ToString(), CultureInfo.InvariantCulture);

                var identity = new ClaimsIdentity(
                    [new Claim("sub", $"{user}-id"), new Claim("preferred_username", user), .. roles.Select(r => new Claim("roles", r))],
                    "test",
                    "preferred_username",
                    "roles");
                var properties = new AuthenticationProperties();
                properties.StoreTokens(
                [
                    new AuthenticationToken { Name = "access_token", Value = "access-1" },
                    new AuthenticationToken { Name = "refresh_token", Value = "refresh-1" },
                    new AuthenticationToken { Name = "id_token", Value = "id-1" },
                    new AuthenticationToken { Name = "expires_at", Value = clock.GetUtcNow().AddSeconds(expiresIn).ToString("o", CultureInfo.InvariantCulture) },
                ]);
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
            }));
            next(app);
        };
    }
}

/// <summary>The services behind the gateway: records each forwarded request; answers 200.</summary>
public sealed class FakeServices : HttpMessageHandler, IForwarderHttpClientFactory
{
    public ConcurrentQueue<HttpRequestMessage> Received { get; } = new();

    public HttpMessageInvoker CreateClient(ForwarderHttpClientContext context) => new(this, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Received.Enqueue(request);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") });
    }
}

/// <summary>Keycloak's token endpoint: renews with the next access token, or refuses.</summary>
public sealed class FakeKeycloak : HttpMessageHandler
{
    public List<Dictionary<string, string>> Requests { get; } = [];

    public string? NextAccessToken { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = (await request.Content!.ReadAsStringAsync(cancellationToken))
            .Split('&')
            .Select(pair => pair.Split('='))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p[1]));
        Requests.Add(form);

        if (NextAccessToken is null)
        {
            return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("""{"error":"invalid_grant"}""", Encoding.UTF8, "application/json") };
        }

        var body = $$"""{"access_token":"{{NextAccessToken}}","refresh_token":"refresh-2","id_token":"id-2","expires_in":900,"token_type":"Bearer"}""";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
