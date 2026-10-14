using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace OrderFlow.ServiceDefaults;

/// <summary>
/// Customer logins (spec 012, ADR 0008): access tokens from the Keycloak realm, validated here
/// (signature from the realm's keys, issuer, audience, lifetime). Settings under <c>Auth</c>:
/// <list type="bullet">
/// <item><c>Authority</c>: the realm, e.g. http://localhost:8180/realms/orderflow.</item>
/// <item><c>MetadataAddress</c>: where to fetch the realm's keys, when it differs (inside compose: http://keycloak:8080/...).</item>
/// <item><c>Issuer</c>: the issuer tokens carry, when it differs from the metadata's (the browser's address).</item>
/// <item><c>Audience</c>: default <c>orderflow-api</c>. <c>RequireHttpsMetadata</c>: default true.</item>
/// </list>
/// </summary>
public static class AuthenticationExtensions
{
    public const string Audience = "orderflow-api";

    /// <summary>The Keycloak realm role for staff (spec 015), in the tokens' flat "roles" claim.</summary>
    public const string StaffRole = "staff";

    public static IHostApplicationBuilder AddOrderFlowAuthentication(this IHostApplicationBuilder builder)
    {
        var auth = builder.Configuration.GetSection("Auth");

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = auth["Authority"];
                if (auth["MetadataAddress"] is { Length: > 0 } metadata)
                {
                    options.MetadataAddress = metadata;
                }

                options.Audience = auth["Audience"] ?? Audience;
                options.RequireHttpsMetadata = auth.GetValue("RequireHttpsMetadata", true);

                // Keep claim names as Keycloak writes them: the customer is "sub" (R-1).
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.TokenValidationParameters.RoleClaimType = "roles"; // spec 015, Q2
                if (auth["Issuer"] is { Length: > 0 } issuer)
                {
                    options.TokenValidationParameters.ValidIssuer = issuer;
                }

                // Browsers cannot set headers on a WebSocket: SignalR sends the token in the query string.
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = context =>
                    {
                        var token = context.Request.Query["access_token"];
                        if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                        {
                            context.Token = token;
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        builder.Services.AddAuthorization();
        return builder;
    }

    /// <summary>A member of staff: the login carries the realm role <see cref="StaffRole"/> (spec 015). Exact match.</summary>
    public static bool IsStaff(this ClaimsPrincipal user) => user.IsInRole(StaffRole);

    /// <summary>The logged-in customer: the token's subject (spec 012, R-1).</summary>
    public static string CustomerId(this ClaimsPrincipal user) =>
        user.FindFirst("sub")?.Value ?? throw new InvalidOperationException("The login has no subject.");
}
