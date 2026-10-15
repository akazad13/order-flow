using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Gateway;
using OrderFlow.Gateway.Persistence;
using OrderFlow.Gateway.Sessions;
using OrderFlow.ServiceDefaults;
using Yarp.ReverseProxy.Transforms;

var builder = WebApplication.CreateBuilder(args);

// Spec 023: the backend for the web app. The browser holds only a session cookie (R-1).
var gateway = builder.Configuration.GetSection(GatewayOptions.Section).Get<GatewayOptions>() ?? new GatewayOptions();
builder.Services.Configure<GatewayOptions>(builder.Configuration.GetSection(GatewayOptions.Section));
var usePostgres = builder.Configuration["Persistence:Provider"] == "Postgres";

builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();
builder.Services.AddSingleton(TimeProvider.System);

// Behind nginx: the browser's scheme and host build Keycloak's return address.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Sessions and the cookie's keys: PostgreSQL in compose (shared by every copy, AC-6), in memory otherwise.
if (usePostgres)
{
    builder.Services.AddDbContext<GatewayDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Gateway")));
    builder.Services.AddDataProtection().SetApplicationName("orderflow-gateway").PersistKeysToDbContext<GatewayDbContext>();
    builder.Services.AddSingleton<ITicketStore, PostgresTicketStore>();
}
else
{
    builder.Services.AddDataProtection().SetApplicationName("orderflow-gateway");
    builder.Services.AddSingleton<ITicketStore, InMemoryTicketStore>();
}

var cookiePolicy = gateway.SecureCookie ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
builder.Services
    .AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        // AC-1: scripts cannot read it; it goes only to the shop; in production only over HTTPS.
        options.Cookie.Name = "orderflow.session";
        options.Cookie.Path = "/"; // for every page and API call, wherever the login ended
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = cookiePolicy;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        // An API call without a session answers 401; it never redirects to a login page.
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = gateway.Authority;
        if (gateway.MetadataAddress is { Length: > 0 } metadata)
        {
            options.MetadataAddress = metadata;
        }

        options.RequireHttpsMetadata = gateway.RequireHttpsMetadata;
        options.ClientId = gateway.ClientId;
        options.ClientSecret = gateway.ClientSecret;
        options.ResponseType = "code";
        // Back from Keycloak with a GET (the default is a form POST): the login's Lax cookies travel
        // with a top-level GET from another site, not with a cross-site POST.
        options.ResponseMode = "query";
        options.UsePkce = true;
        options.SaveTokens = true; // into the server-side session, never to the browser (R-1)
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.CallbackPath = "/bff/signin-oidc";
        options.SignedOutCallbackPath = "/bff/signout-callback-oidc";
        options.GetClaimsFromUserInfoEndpoint = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "preferred_username";
        options.TokenValidationParameters.RoleClaimType = "roles";
        // Keycloak returns with a top-level GET: Lax is enough, and works without HTTPS in development.
        options.CorrelationCookie.SameSite = SameSiteMode.Lax;
        options.CorrelationCookie.SecurePolicy = cookiePolicy;
        options.NonceCookie.SameSite = SameSiteMode.Lax;
        options.NonceCookie.SecurePolicy = cookiePolicy;
    });

builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme)
    .Configure<ITicketStore>((options, store) => options.SessionStore = store);

builder.Services.AddHttpClient(TokenRefresher.HttpClientName);
builder.Services.AddSingleton<TokenRefresher>();

// The page's API calls and live connection (routes in appsettings.json), with the session's token
// as the bearer token, and without the browser's cookies (the services need neither).
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"))
    .AddTransforms(transforms => transforms.AddRequestTransform(context =>
    {
        context.ProxyRequest.Headers.Remove("Cookie");
        if (context.HttpContext.Items[SessionTokens.AccessTokenKey] is string token)
        {
            context.ProxyRequest.Headers.Authorization = new("Bearer", token);
        }

        return ValueTask.CompletedTask;
    }));

var app = builder.Build();

if (usePostgres)
{
    await app.MigrateAsync<GatewayDbContext>(); // one copy at a time (spec 013)
}

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.MapHealthChecks("/health");
app.UseAuthentication();
app.MapBffEndpoints();
app.MapReverseProxy(proxy =>
{
    proxy.UseMiddleware<CrossSiteGuard>();
    proxy.UseMiddleware<SessionTokens>();
});

app.Run();

public partial class Program;
