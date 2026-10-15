using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;
using OrderFlow.ServiceDefaults;

namespace OrderFlow.Gateway;

/// <summary>The page's logged-in user, as it may see it (spec 023): no tokens, ever (R-1).</summary>
public sealed record UserInfo(string Id, string Name, bool IsStaff, bool IsAdmin);

/// <summary>Logging in and out, and who is logged in (spec 023, AC-2).</summary>
public static class BffEndpoints
{
    public static IEndpointRouteBuilder MapBffEndpoints(this IEndpointRouteBuilder app)
    {
        var bff = app.MapGroup("/bff");

        // Starts Keycloak's login; Keycloak comes back to /bff/signin-oidc, then to the page.
        bff.MapGet("/login", (string? returnUrl) =>
            Results.Challenge(new AuthenticationProperties { RedirectUri = Local(returnUrl) }, [OpenIdConnectDefaults.AuthenticationScheme]));

        bff.MapGet("/user", async (HttpContext context) =>
        {
            var session = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            if (!session.Succeeded || session.Principal is not { } user)
            {
                return Results.Unauthorized();
            }

            return Results.Ok(new UserInfo(user.CustomerId(), user.Identity?.Name ?? user.CustomerId(), user.IsStaff(), user.IsInRole(AuthenticationExtensions.AdminRole)));
        });

        // Ends the session here and returns Keycloak's logout address, which the page then opens.
        bff.MapPost("/logout", async (HttpContext context, IOptionsMonitor<OpenIdConnectOptions> oidc) =>
        {
            if (context.Request.Headers["X-CSRF"] != "1")
            {
                return Results.StatusCode(StatusCodes.Status403Forbidden);
            }

            var session = await context.AuthenticateAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            var settings = oidc.Get(OpenIdConnectDefaults.AuthenticationScheme);
            var configuration = await settings.ConfigurationManager!.GetConfigurationAsync(context.RequestAborted);
            var home = $"{context.Request.Scheme}://{context.Request.Host}/";
            var query = new Dictionary<string, string?>
            {
                ["post_logout_redirect_uri"] = home,
                ["client_id"] = settings.ClientId,
                ["id_token_hint"] = session.Properties?.GetTokenValue("id_token"),
            };
            var logoutUrl = Microsoft.AspNetCore.WebUtilities.QueryHelpers.AddQueryString(configuration.EndSessionEndpoint, query.Where(p => p.Value is not null)!);
            return Results.Ok(new { logoutUrl });
        });

        return app;
    }

    /// <summary>Only a path on this site: never an address elsewhere (an open redirect).</summary>
    private static string Local(string? returnUrl) =>
        returnUrl is { Length: > 0 } url && url.StartsWith('/') && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
            ? url
            : "/";
}
