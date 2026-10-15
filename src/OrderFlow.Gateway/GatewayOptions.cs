namespace OrderFlow.Gateway;

/// <summary>Settings under <c>Gateway</c> (spec 023).</summary>
public sealed class GatewayOptions
{
    public const string Section = "Gateway";

    /// <summary>The Keycloak realm, as the browser reaches it (the tokens' issuer).</summary>
    public string Authority { get; set; } = "http://localhost:8180/realms/orderflow";

    /// <summary>Where the gateway itself reads the realm's metadata, when it differs (inside compose).</summary>
    public string? MetadataAddress { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    public string ClientId { get; set; } = "orderflow-gateway";

    public string? ClientSecret { get; set; }

    /// <summary>Renew the access token when it expires within this time (AC-4). Default one minute.</summary>
    public TimeSpan RefreshBefore { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>The shop's own origins, the only ones allowed to open the live connection (AC-5).</summary>
    public string[] AllowedOrigins { get; set; } = ["http://localhost:5180", "http://localhost:5173"];

    /// <summary>Send the session cookie over HTTPS only (production, AC-7).</summary>
    public bool SecureCookie { get; set; }
}
