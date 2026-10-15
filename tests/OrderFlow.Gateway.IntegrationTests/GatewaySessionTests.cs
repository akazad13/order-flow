using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Gateway.Persistence;
using OrderFlow.IntegrationTesting;

namespace OrderFlow.Gateway.IntegrationTests;

/// <summary>
/// Spec 023, AC-6, against real PostgreSQL: sessions and the keys that protect the cookie are shared,
/// so any copy of the gateway serves any session, and a session survives a restart.
/// </summary>
[Collection(IntegrationCollection.Name)]
[Trait("Category", "Integration")]
public sealed class GatewaySessionTests(InfrastructureFixture infrastructure)
{
    [Fact]
    public async Task Any_copy_serves_any_session_and_the_tokens_stay_in_the_database()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "Postgres",
            ["ConnectionStrings:Gateway"] = infrastructure.ConnectionString($"gateway_{Guid.NewGuid():N}"),
            ["Gateway:ClientSecret"] = "test-secret",
        };

        string cookie;
        await using (var first = new GatewayHost(settings))
        {
            var signIn = await first.CreateClient().PostAsync("/test/sign-in?user=alice", null);
            signIn.EnsureSuccessStatusCode();
            cookie = signIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("orderflow.session=", StringComparison.Ordinal)).Split(';')[0];

            await using var second = new GatewayHost(settings);
            (await WhoIsLoggedIn(second, cookie)).ShouldBe("alice", "the second copy, with the first copy's cookie");

            // The cookie carries only the session's key; the tokens are in the database (R-1).
            await using var scope = first.Services.CreateAsyncScope();
            var sessions = await scope.ServiceProvider.GetRequiredService<GatewayDbContext>().Sessions.ToListAsync();
            sessions.Count.ShouldBe(1);
            cookie.Length.ShouldBeLessThan(sessions[0].Ticket.Length + 100);
        }

        await using var restarted = new GatewayHost(settings);
        (await WhoIsLoggedIn(restarted, cookie)).ShouldBe("alice", "after every copy restarted");
    }

    private static async Task<string?> WhoIsLoggedIn(GatewayHost host, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/bff/user");
        request.Headers.Add("Cookie", cookie);
        var response = await host.CreateClient().SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            return null;
        }

        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("name").GetString();
    }

    /// <summary>The real gateway, with a test-only sign-in standing in for Keycloak's login.</summary>
    private sealed class GatewayHost(IReadOnlyDictionary<string, string?> settings) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<IStartupFilter, TestSignIn>();
                // Unless the gateway shares its keys itself, each copy keeps its own, as separate
                // containers would. (On a developer machine the default store is the user profile,
                // which every copy in this process would otherwise share and so hide the problem.)
                services.PostConfigure<KeyManagementOptions>(options => options.XmlRepository ??= new PrivateKeys());
            });
        }
    }

    private sealed class PrivateKeys : IXmlRepository
    {
        private readonly List<XElement> _keys = [];

        public IReadOnlyCollection<XElement> GetAllElements() => _keys.ToList();

        public void StoreElement(XElement element, string friendlyName) => _keys.Add(element);
    }

    private sealed class TestSignIn : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path != "/test/sign-in")
                {
                    await nextMiddleware();
                    return;
                }

                var user = context.Request.Query["user"].ToString();
                var identity = new ClaimsIdentity([new Claim("sub", $"{user}-id"), new Claim("preferred_username", user)], "test", "preferred_username", "roles");
                var properties = new AuthenticationProperties();
                properties.StoreTokens(
                [
                    new AuthenticationToken { Name = "access_token", Value = new string('a', 1200) },
                    new AuthenticationToken { Name = "refresh_token", Value = new string('r', 600) },
                    new AuthenticationToken { Name = "expires_at", Value = DateTimeOffset.UtcNow.AddMinutes(15).ToString("o") },
                ]);
                await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity), properties);
            });
            next(app);
        };
    }
}
