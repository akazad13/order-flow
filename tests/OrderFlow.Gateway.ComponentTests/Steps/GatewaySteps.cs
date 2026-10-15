using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using OrderFlow.Gateway.ComponentTests.Support;
using Reqnroll;
using Shouldly;

namespace OrderFlow.Gateway.ComponentTests.Steps;

/// <summary>Spec 023: what the browser holds, and what the gateway forwards.</summary>
[Binding]
public sealed class GatewaySteps(GatewayFactory gateway)
{
    private HttpClient? _browser;
    private HttpResponseMessage? _response;
    private string? _setCookie;

    private HttpClient Browser => _browser ??= gateway.Browser();

    [Given("the gateway sends its cookie over HTTPS only")]
    public void GivenSecureCookies() => gateway.SecureCookie = true;

    [Given("{string} is logged in, with an access token that expires in {int} minutes")]
    public Task GivenLoggedIn(string user, int minutes) => SignIn(user, "", minutes);

    [Given("{string} is logged in with the roles {string}, with an access token that expires in {int} minutes")]
    public Task GivenLoggedInWithRoles(string user, string roles, int minutes) => SignIn(user, roles, minutes);

    [Given("Keycloak will renew it as {string}")]
    public void GivenKeycloakRenews(string accessToken) => gateway.TokenEndpoint.NextAccessToken = accessToken;

    [Given("Keycloak will refuse to renew it")]
    public void GivenKeycloakRefuses() => gateway.TokenEndpoint.NextAccessToken = null;

    [When("{int} minutes and {int} seconds pass")]
    public void WhenTimePasses(int minutes, int seconds) => gateway.Clock.Advance(new TimeSpan(0, minutes, seconds));

    [When("{int} minutes pass")]
    public void WhenMinutesPass(int minutes) => gateway.Clock.Advance(TimeSpan.FromMinutes(minutes));

    [When("the page calls the Order API without a session")]
    [When("the page calls the Order API")]
    public async Task WhenThePageCallsTheOrderApi() => _response = await Send(HttpMethod.Get, "/order-api/orders/x", csrf: true);

    [When("another site makes the page call the Order API, without the X-CSRF header")]
    public async Task WhenAnotherSiteCalls() => _response = await Send(HttpMethod.Post, "/order-api/orders", csrf: false);

    [When("the page opens the live connection from {string}")]
    public async Task WhenThePageOpensTheLiveConnection(string origin)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/order-api/hubs/orders");
        request.Headers.Add("Origin", origin);
        _response = await Browser.SendAsync(request);
    }

    [When("the page asks who is logged in")]
    public async Task WhenThePageAsksWhoIsLoggedIn() => _response = await Browser.GetAsync("/bff/user");

    [When("the page asks to log in")]
    public async Task WhenThePageAsksToLogIn() => _response = await Browser.GetAsync("/bff/login?returnUrl=/");

    [When("the page logs out")]
    public async Task WhenThePageLogsOut() => _response = await Send(HttpMethod.Post, "/bff/logout", csrf: true);

    [Then("the answer is {int}")]
    public void ThenTheAnswerIs(int status) => ((int)_response!.StatusCode).ShouldBe(status);

    [Then("nothing was forwarded")]
    public void ThenNothingWasForwarded() => gateway.Downstream.Received.ShouldBeEmpty();

    [Then("the Order service received the bearer token {string}")]
    public void ThenTheOrderServiceReceivedTheToken(string token) => Tokens().ShouldBe([token]);

    [Then("the Order service received the bearer tokens {string} and {string}")]
    public void ThenTheOrderServiceReceivedTokens(string first, string second) => Tokens().ShouldBe([first, second]);

    [Then("the Order service received no cookie")]
    public void ThenNoCookie() => gateway.Downstream.Received.ShouldAllBe(r => !r.Headers.Contains("Cookie"));

    [Then("Keycloak was asked to renew once, with the refresh token {string}")]
    public void ThenKeycloakWasAskedOnce(string refreshToken)
    {
        gateway.TokenEndpoint.Requests.Count.ShouldBe(1);
        var request = gateway.TokenEndpoint.Requests[0];
        request["grant_type"].ShouldBe("refresh_token");
        request["refresh_token"].ShouldBe(refreshToken);
        request["client_id"].ShouldBe("orderflow-gateway");
        request["client_secret"].ShouldBe("test-secret");
    }

    [Then("it is told {string}, staff but not admin")]
    public async Task ThenItIsTold(string name)
    {
        _response!.StatusCode.ShouldBe(HttpStatusCode.OK);
        var user = await _response.Content.ReadFromJsonAsync<JsonElement>();
        user.GetProperty("name").GetString().ShouldBe(name);
        user.GetProperty("isStaff").GetBoolean().ShouldBeTrue();
        user.GetProperty("isAdmin").GetBoolean().ShouldBeFalse();
    }

    [Then("the answer contains no token")]
    public async Task ThenTheAnswerContainsNoToken()
    {
        var body = await _response!.Content.ReadAsStringAsync();
        body.ShouldNotContain("access-1");
        body.ShouldNotContain("refresh-1");
        body.ShouldNotContain("id-1");
        body.ShouldNotContain("token", Case.Insensitive);
    }

    [Then("the session cookie is HttpOnly and SameSite=Lax, and not Secure-only")]
    public void ThenTheCookieFlags()
    {
        _setCookie.ShouldNotBeNull();
        _setCookie.ShouldStartWith("orderflow.session=");
        _setCookie.ShouldContain("httponly", Case.Insensitive);
        _setCookie.ShouldContain("samesite=lax", Case.Insensitive);
        _setCookie.Split(';').Select(p => p.Trim().ToLowerInvariant()).ShouldNotContain("secure");
    }

    [Then("the session cookie is Secure-only")]
    public void ThenTheCookieIsSecure() => _setCookie!.Split(';').Select(p => p.Trim().ToLowerInvariant()).ShouldContain("secure");

    [Then("no token is in the session cookie")]
    public void ThenNoTokenInTheCookie()
    {
        // The cookie holds only the session's key, protected; the tokens stay on the server (R-1).
        var value = _setCookie!.Split(';')[0]["orderflow.session=".Length..];
        value.Length.ShouldBeLessThan(1000);
        value.ShouldNotContain("access-1");
    }

    [Then("it is sent to Keycloak's login with a PKCE challenge, returning to {string}")]
    public void ThenItIsSentToKeycloak(string callback)
    {
        _response!.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = _response.Headers.Location!.ToString();
        location.ShouldStartWith($"{GatewayFactory.Keycloak}/auth?");
        location.ShouldContain("client_id=orderflow-gateway");
        location.ShouldContain("code_challenge=");
        location.ShouldContain("code_challenge_method=S256");
        location.ShouldNotContain("response_mode=form_post"); // back with a GET (query is the default), which Lax cookies survive
        location.ShouldContain($"redirect_uri={Uri.EscapeDataString("http://localhost" + callback)}");
    }

    [Then("it is given Keycloak's logout address, with the ID token as a hint")]
    public async Task ThenItIsGivenTheLogoutAddress()
    {
        _response!.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await _response.Content.ReadFromJsonAsync<JsonElement>();
        var url = body.GetProperty("logoutUrl").GetString()!;
        url.ShouldStartWith($"{GatewayFactory.Keycloak}/logout?");
        url.ShouldContain("id_token_hint=id-1");
        url.ShouldContain($"post_logout_redirect_uri={Uri.EscapeDataString("http://localhost/")}");
    }

    private async Task SignIn(string user, string roles, int minutes)
    {
        var response = await Browser.PostAsync($"/test/sign-in?user={user}&roles={roles}&expiresIn={minutes * 60}", null);
        response.EnsureSuccessStatusCode();
        _setCookie = response.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("orderflow.session=", StringComparison.Ordinal));
    }

    private async Task<HttpResponseMessage> Send(HttpMethod method, string path, bool csrf)
    {
        using var request = new HttpRequestMessage(method, path);
        if (csrf)
        {
            request.Headers.Add("X-CSRF", "1");
        }

        return await Browser.SendAsync(request);
    }

    private List<string?> Tokens() =>
        gateway.Downstream.Received.Select(r => r.Headers.Authorization is { Scheme: "Bearer" } auth ? auth.Parameter : null).ToList();
}
