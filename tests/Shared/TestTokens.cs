using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace OrderFlow.Testing;

/// <summary>
/// Logins for tests below E2E (spec 012, Q4; ADR 0008). A test host calls
/// <see cref="TrustTestTokens"/> to swap Keycloak for a test-only signing key; the services have
/// no setting that does this, so it cannot be switched on outside tests. Linked into each test
/// project that needs it.
/// </summary>
internal static class TestTokens
{
    public const string Issuer = "https://orderflow.test/realms/orderflow";
    public const string Audience = "orderflow-api";

    private static readonly SymmetricSecurityKey Key = new(Encoding.UTF8.GetBytes("orderflow-test-signing-key-tests-only-0001"));
    private static readonly SymmetricSecurityKey OtherKey = new(Encoding.UTF8.GetBytes("someone-elses-signing-key-tests-only-0002"));

    /// <summary>A valid access token for the customer (the token's subject is the customer ID), with realm roles in "roles" as Keycloak's mapper writes them (spec 015).</summary>
    public static string For(string customerId, params string[] roles) => Create(customerId, Key, Issuer, Audience, DateTime.UtcNow.AddMinutes(10), roles);

    /// <summary>A member of staff (spec 015): a login with the realm role "staff".</summary>
    public static string Staff(string userId) => For(userId, "staff");

    /// <summary>Tokens that must be refused (spec 012, AC-5), by name as the scenarios say it.</summary>
    public static string Bad(string customerId, string problem) => problem switch
    {
        "expired" => Create(customerId, Key, Issuer, Audience, DateTime.UtcNow.AddMinutes(-10)),
        "signed by another key" => Create(customerId, OtherKey, Issuer, Audience, DateTime.UtcNow.AddMinutes(10)),
        "from another issuer" => Create(customerId, Key, "https://evil.example/realms/orderflow", Audience, DateTime.UtcNow.AddMinutes(10)),
        "for another audience" => Create(customerId, Key, Issuer, "account", DateTime.UtcNow.AddMinutes(10)),
        _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, "Unknown kind of bad token."),
    };

    /// <summary>Validate tokens against the test key instead of the identity server.</summary>
    public static IServiceCollection TrustTestTokens(this IServiceCollection services) =>
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.Authority = null;
            options.MetadataAddress = null!;
            options.ConfigurationManager = null;
            options.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
            options.TokenValidationParameters.ValidIssuer = Issuer;
            options.TokenValidationParameters.ValidAudience = Audience;
            options.TokenValidationParameters.IssuerSigningKey = Key;
            options.TokenValidationParameters.ClockSkew = TimeSpan.Zero;
        });

    public static HttpClient LoggedInAs(this HttpClient client, string customerId) => client.WithToken(For(customerId));

    public static HttpClient WithToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string Create(string customerId, SecurityKey key, string issuer, string audience, DateTime expires, string[]? roles = null) =>
        new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity([new Claim("sub", customerId), new Claim("preferred_username", customerId), .. (roles ?? []).Select(r => new Claim("roles", r))]),
            Issuer = issuer,
            Audience = audience,
            NotBefore = expires.AddMinutes(-30),
            IssuedAt = expires.AddMinutes(-30),
            Expires = expires,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
        });
}
