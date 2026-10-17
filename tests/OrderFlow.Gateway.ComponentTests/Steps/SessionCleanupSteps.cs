using OrderFlow.Gateway.ComponentTests.Support;
using Reqnroll;
using Shouldly;

namespace OrderFlow.Gateway.ComponentTests.Steps;

/// <summary>
/// Step definitions for gateway-session-cleanup.feature (spec 024):
/// expired sessions are removed hourly; sessions in use keep their expiry alive.
/// </summary>
[Binding]
public sealed class SessionCleanupSteps(GatewayContext ctx, GatewayFactory factory)
{
    // ─── When ────────────────────────────────────────────────────────────────

    [When("{int} hours pass, and the session cleanup runs")]
    public async Task WhenHoursPassAndCleanupRuns(int hours)
    {
        // Kick the app into life (ensures the background service is running) by making a request.
        _ = factory.CreateClient();

        // Advance the clock one hour at a time so the PeriodicTimer fires once per interval.
        // Capture NextCleanup *before* each advance so we hold the right TCS.
        for (var i = 0; i < hours; i++)
        {
            var next = factory.Cleanup.NextCleanup;
            factory.Clock.Advance(TimeSpan.FromHours(1));
            await next.WaitAsync(TimeSpan.FromSeconds(10));
        }
    }

    // ─── Then ────────────────────────────────────────────────────────────────

    [Then("the gateway holds {int} sessions")]
    public void ThenGatewayHoldsSessions(int count) =>
        factory.Sessions.Count.ShouldBe(count);

    [Then("the gateway holds {int} session")]
    public void ThenGatewayHoldsSession(int count) =>
        factory.Sessions.Count.ShouldBe(count);

    [Then("the page asks who is logged in")]
    public async Task ThenPageAsksWhoIsLoggedIn()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/bff/user");
        request.Headers.Add("X-CSRF", "1");
        ctx.LastResponse?.Dispose();
        ctx.LastResponse = await ctx.Browser.SendAsync(request);
    }
}
