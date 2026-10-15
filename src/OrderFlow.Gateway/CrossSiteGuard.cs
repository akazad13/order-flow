using Microsoft.Extensions.Options;

namespace OrderFlow.Gateway;

/// <summary>
/// Refuses calls another site could make with the shop's cookie (spec 023, AC-5): API calls must carry
/// <c>X-CSRF: 1</c>, which a cross-site form cannot send; the live connection (a WebSocket, which
/// cannot carry custom headers) must come from one of the shop's own origins.
/// </summary>
public sealed class CrossSiteGuard(RequestDelegate next, IOptions<GatewayOptions> options)
{
    public const string HubPrefix = "/order-api/hubs";

    public Task InvokeAsync(HttpContext context)
    {
        var allowed = context.Request.Path.StartsWithSegments(HubPrefix)
            ? options.Value.AllowedOrigins.Contains(context.Request.Headers.Origin.ToString(), StringComparer.OrdinalIgnoreCase)
            : context.Request.Headers["X-CSRF"] == "1";

        if (allowed)
        {
            return next(context);
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
