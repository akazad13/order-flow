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
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;
using OrderFlow.Gateway.Persistence;
using OrderFlow.Gateway.Sessions;
using OrderFlow.IntegrationTesting;

namespace OrderFlow.Gateway.IntegrationTests;

/// <summary>
/// Spec 023, AC-6, against real PostgreSQL: sessions and the keys that protect the cookie are shared,
/// so any copy of the gateway serves any session, and a session survives a restart. Spec 024: two
/// copies remove the expired sessions together, and keep the ones in use.
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

    [Fact]
    public async Task Two_copies_remove_the_expired_sessions_and_keep_the_ones_in_use()
    {
        // Far from the real date: expiries that followed the wall clock instead would show.
        var clock = new FakeTimeProvider(new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var settings = new Dictionary<string, string?>
        {
            ["Persistence:Provider"] = "Postgres",
            ["ConnectionStrings:Gateway"] = infrastructure.ConnectionString($"gateway_{Guid.NewGuid():N}"),
            ["Gateway:ClientSecret"] = "test-secret",
        };
        await using var first = new GatewayHost(settings, clock);
        await using var second = new GatewayHost(settings, clock);

        var alice = await SignIn(first, "alice"); // unused: expires after eight hours
        var carol = await SignIn(first, "carol"); // used after five hours: her expiry moves forward
        (await Cleanup(first, second, TimeSpan.FromHours(5))).ShouldBe(0);
        (await WhoIsLoggedIn(second, carol)).ShouldBe("carol");
        var bob = await SignIn(second, "bob"); // logged in after five hours

        (await Cleanup(first, second, TimeSpan.FromHours(4))).ShouldBe(1, "alice's session, removed once between both copies");

        await using var scope = first.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<GatewayDbContext>().Sessions.CountAsync()).ShouldBe(2);
        (await WhoIsLoggedIn(first, alice)).ShouldBeNull();
        (await WhoIsLoggedIn(first, carol)).ShouldBe("carol");
        (await WhoIsLoggedIn(second, bob)).ShouldBe("bob");
    }

    /// <summary>Moves the clock, and returns how many sessions both copies' cleanups removed.</summary>
    private static async Task<int> Cleanup(GatewayHost first, GatewayHost second, TimeSpan passing)
    {
        var cleanups = new[] { first, second }
            .Select(host => host.Services.GetServices<IHostedService>().OfType<ExpiredSessionsCleanup>().Single().NextCleanup)
            .ToArray();
        ((FakeTimeProvider)first.Services.GetRequiredService<TimeProvider>()).Advance(passing);
        return (await Task.WhenAll(cleanups).WaitAsync(TimeSpan.FromSeconds(30))).Sum();
    }

    private static async Task<string> SignIn(GatewayHost host, string user)
    {
        var signIn = await host.CreateClient().PostAsync($"/test/sign-in?user={user}", null);
        signIn.EnsureSuccessStatusCode();
        return signIn.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("orderflow.session=", StringComparison.Ordinal)).Split(';')[0];
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
    private sealed class GatewayHost(IReadOnlyDictionary<string, string?> settings, TimeProvider? clock = null) : WebApplicationFactory<Program>
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
                if (clock is not null)
                {
                    services.AddSingleton(clock);
                }

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
