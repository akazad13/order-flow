using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace OrderFlow.Gateway.Sessions;

/// <summary>
/// Before a call is forwarded (spec 023, AC-3, AC-4): the session must exist and have a fresh access
/// token, renewed here if needed; otherwise the session ends and the call answers 401. The token is
/// left for the proxy, which adds it as the bearer token.
/// </summary>
public sealed class SessionTokens(RequestDelegate next, TokenRefresher refresher, ILogger<SessionTokens> logger)
{
    public const string AccessTokenKey = "orderflow.access_token";

    public async Task InvokeAsync(HttpContext context)
    {
        var session = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        if (!session.Succeeded || session.Properties is null || session.Principal is null)
        {
            if (context.Request.Cookies.ContainsKey("orderflow.session"))
            {
                logger.LogInformation("A session cookie that matches no session: {Reason}", session.Failure?.Message ?? "no session for its key");
            }

            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        var (token, renewed) = await refresher.EnsureFreshAsync(session.Properties, context.RequestAborted);
        if (token is null)
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        if (renewed)
        {
            // The same session, with the new tokens (the store renews it under its key).
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, session.Principal, session.Properties);
        }

        context.Items[AccessTokenKey] = token;
        await next(context);
    }
}
