using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Web;
using OrderFlow.Gateway.ComponentTests.Support;
using Reqnroll;
using Shouldly;

namespace OrderFlow.Gateway.ComponentTests.Steps;

/// <summary>
/// Step definitions for gateway.feature (spec 023): tokens on the server, CSRF guard,
/// login/logout, cookie security, live-connection origin check.
/// </summary>
[Binding]
public sealed class GatewaySteps(GatewayContext ctx, GatewayFactory factory)
{
    // ─── Given ───────────────────────────────────────────────────────────────

    [Given("{string} is logged in, with an access token that expires in {int} minutes")]
    public Task GivenUserIsLoggedIn(string user, int minutes) =>
        ctx.SignInAsync(user, expiresInSeconds: minutes * 60);

    [Given("{string} is logged in with the roles {string}, with an access token that expires in {int} minutes")]
    public Task GivenUserIsLoggedInWithRoles(string user, string roles, int minutes) =>
        ctx.SignInAsync(user, expiresInSeconds: minutes * 60, roles: roles);

    [Given("Keycloak will renew it as {string}")]
    public void GivenKeycloakWillRenewAs(string nextAccessToken) =>
        factory.TokenEndpoint.NextAccessToken = nextAccessToken;

    [Given("Keycloak will refuse to renew it")]
    public void GivenKeycloakWillRefuse() =>
        factory.TokenEndpoint.NextAccessToken = null;   // FakeKeycloak returns 400 when null

    [Given("the gateway sends its cookie over HTTPS only")]
    public void GivenSecureCookie() =>
        factory.SecureCookie = true;

    // ─── When ────────────────────────────────────────────────────────────────

    [When("the page calls the Order API")]
    public async Task WhenPageCallsOrderApi()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/order-api/orders");
        request.Headers.Add("X-CSRF", "1");
        ctx.LastResponse = await ctx.Browser.SendAsync(request);
    }

    [When("the page calls the Order API without a session")]
    public async Task WhenPageCallsOrderApiWithoutSession()
    {
        // A fresh client with no cookies simulates having no session.
        using var anonClient = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        var request = new HttpRequestMessage(HttpMethod.Get, "/order-api/orders");
        request.Headers.Add("X-CSRF", "1");
        ctx.LastResponse = await anonClient.SendAsync(request);
    }

    [When("another site makes the page call the Order API, without the X-CSRF header")]
    public async Task WhenAnotherSiteMakesCallWithoutCsrfHeader()
    {
        // The browser's session cookie is sent, but the X-CSRF header is absent.
        var request = new HttpRequestMessage(HttpMethod.Get, "/order-api/orders");
        ctx.LastResponse = await ctx.Browser.SendAsync(request);
    }

    [When("the page asks who is logged in")]
    public async Task WhenPageAsksWhoIsLoggedIn()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/bff/user");
        request.Headers.Add("X-CSRF", "1");
        ctx.LastResponse = await ctx.Browser.SendAsync(request);
    }

    [When("the page asks to log in")]
    public async Task WhenPageAsksToLogIn()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/bff/login");
        ctx.LastResponse = await ctx.Browser.SendAsync(request);
    }

    [When("the page logs out")]
    public async Task WhenPageLogsOut()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/bff/logout");
        request.Headers.Add("X-CSRF", "1");
        ctx.LastResponse = await ctx.Browser.SendAsync(request);
    }

    [When("{int} minutes pass")]
    public void WhenMinutesPass(int minutes) =>
        factory.Clock.Advance(TimeSpan.FromMinutes(minutes));

    [When("{int} minutes and {int} seconds pass")]
    public void WhenMinutesAndSecondsPass(int minutes, int seconds) =>
        factory.Clock.Advance(TimeSpan.FromMinutes(minutes) + TimeSpan.FromSeconds(seconds));

    [When("the page opens the live connection from {string}")]
    public async Task WhenPageOpensLiveConnection(string origin)
    {
        // The live connection path is a WebSocket upgrade, but the CORS-origin check is on the
        // HTTP Upgrade request's Origin header. We test the guard using a plain GET here.
        var request = new HttpRequestMessage(HttpMethod.Get, "/order-api/hubs/orders");
        request.Headers.Add("Origin", origin);
        ctx.LastResponse = await ctx.Browser.SendAsync(request);
    }

    // ─── Then ────────────────────────────────────────────────────────────────

    [Then("the answer is {int}")]
    public void ThenAnswerIs(int statusCode) =>
        ((int)ctx.LastResponse!.StatusCode).ShouldBe(statusCode);

    [Then("nothing was forwarded")]
    public void ThenNothingWasForwarded() =>
        factory.Downstream.Received.ShouldBeEmpty();

    [Then("the Order service received the bearer token {string}")]
    public void ThenOrderServiceReceivedBearerToken(string token)
    {
        var received = factory.Downstream.Received.ToArray();
        received.ShouldNotBeEmpty();
        received.Last().Headers.Authorization?.Parameter.ShouldBe(token);
    }

    [Then("the Order service received the bearer tokens {string} and {string}")]
    public void ThenOrderServiceReceivedBearerTokens(string first, string second)
    {
        var received = factory.Downstream.Received.ToArray();
        received.Length.ShouldBe(2);
        received[0].Headers.Authorization?.Parameter.ShouldBe(first);
        received[1].Headers.Authorization?.Parameter.ShouldBe(second);
    }

    [Then("the Order service received no cookie")]
    public void ThenOrderServiceReceivedNoCookie()
    {
        var received = factory.Downstream.Received.ToArray();
        received.ShouldNotBeEmpty();
        received.Last().Headers.Contains("Cookie").ShouldBeFalse();
    }

    [Then("Keycloak was asked to renew once, with the refresh token {string}")]
    public void ThenKeycloakWasAskedToRenewOnce(string refreshToken)
    {
        factory.TokenEndpoint.Requests.Count.ShouldBe(1);
        factory.TokenEndpoint.Requests[0]["refresh_token"].ShouldBe(refreshToken);
    }

    [Then("it is told {string}, staff but not admin")]
    public async Task ThenItIsToldStaffButNotAdmin(string name)
    {
        ctx.LastResponse!.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ctx.LastResponse.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("name").GetString().ShouldBe(name);
        body.GetProperty("isStaff").GetBoolean().ShouldBeTrue();
        body.GetProperty("isAdmin").GetBoolean().ShouldBeFalse();
    }

    [Then("the answer contains no token")]
    public async Task ThenAnswerContainsNoToken()
    {
        var body = (await ctx.LastResponse!.Content.ReadAsStringAsync()).ToLowerInvariant();
        body.ShouldNotContain("token");
    }

    [Then("the session cookie is HttpOnly and SameSite=Lax, and not Secure-only")]
    public void ThenSessionCookieIsHttpOnlyLaxNotSecure()
    {
        var setCookie = GetSessionSetCookieHeader();
        setCookie.ShouldNotBeNull("session cookie was not set");
        setCookie.ShouldContain("HttpOnly", Case.Insensitive);
        setCookie.ShouldContain("SameSite=Lax", Case.Insensitive);
        setCookie.ShouldNotContain("Secure", Case.Insensitive);
    }

    [Then("the session cookie is Secure-only")]
    public void ThenSessionCookieIsSecureOnly()
    {
        var setCookie = GetSessionSetCookieHeader();
        setCookie.ShouldNotBeNull("session cookie was not set");
        setCookie.ShouldContain("Secure", Case.Insensitive);
    }

    [Then("no token is in the session cookie")]
    public void ThenNoTokenInSessionCookie()
    {
        var setCookie = GetSessionSetCookieHeader();
        setCookie.ShouldNotBeNull("session cookie was not set");
        // The cookie value must not contain any JWT-like content (dots and base64).
        // We just check that "access" and "bearer" don't appear.
        setCookie.ShouldNotContain("access_token", Case.Insensitive);
        setCookie.ShouldNotContain("refresh_token", Case.Insensitive);
    }

    [Then("it is given Keycloak's logout address, with the ID token as a hint")]
    public async Task ThenGivenKeycloakLogoutAddress()
    {
        ctx.LastResponse!.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await ctx.LastResponse.Content.ReadFromJsonAsync<JsonElement>();
        var logoutUrl = body.GetProperty("logoutUrl").GetString();
        logoutUrl.ShouldNotBeNullOrEmpty();
        logoutUrl.ShouldContain(GatewayFactory.Keycloak + "/logout", Case.Sensitive);
        logoutUrl.ShouldContain("id_token_hint=id-1", Case.Sensitive);
    }

    [Then("it is sent to Keycloak's login with a PKCE challenge, returning to {string}")]
    public void ThenSentToKeycloakLoginWithPkce(string returnPath)
    {
        ctx.LastResponse!.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = ctx.LastResponse.Headers.Location?.ToString();
        location.ShouldNotBeNullOrEmpty();
        location.ShouldContain(GatewayFactory.Keycloak + "/auth", Case.Sensitive);
        location.ShouldContain("code_challenge", Case.Insensitive);   // PKCE
        location.ShouldContain(Uri.EscapeDataString(returnPath), Case.Sensitive);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the raw Set-Cookie header for the session cookie that was written during sign-in.
    /// We ask for the sign-in response, which carries the Set-Cookie header.
    /// </summary>
    private string? GetSessionSetCookieHeader()
    {
        // After sign-in the browser already has the cookie. To inspect the Set-Cookie header we
        // issue a fresh sign-in here and capture its response headers directly.
        using var bare = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false
        });
        var response = bare.PostAsync("/test/sign-in?user=probe&expiresIn=900&roles=", content: null).GetAwaiter().GetResult();
        if (response.Headers.TryGetValues("Set-Cookie", out var values))
        {
            return values.FirstOrDefault(v => v.StartsWith("orderflow.session", StringComparison.OrdinalIgnoreCase));
        }

        return null;
    }
}
