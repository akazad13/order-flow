using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.Options;

namespace OrderFlow.Gateway.Sessions;

/// <summary>
/// Keeps a session's access token fresh (spec 023, AC-4): when it expires within
/// <see cref="GatewayOptions.RefreshBefore"/>, the refresh token buys new tokens from Keycloak's token
/// endpoint, and they replace the old ones in the session. Only the gateway talks to that endpoint (R-1).
/// </summary>
public sealed class TokenRefresher(
    IHttpClientFactory http,
    IOptionsMonitor<OpenIdConnectOptions> oidc,
    IOptions<GatewayOptions> options,
    TimeProvider clock,
    ILogger<TokenRefresher> logger)
{
    public const string HttpClientName = "keycloak-token";

    /// <summary>The access token to send, renewing it if it is about to expire; null if the session can no longer have one.</summary>
    public async Task<(string? AccessToken, bool Renewed)> EnsureFreshAsync(AuthenticationProperties properties, CancellationToken cancellationToken)
    {
        var access = properties.GetTokenValue("access_token");
        var expiresAt = properties.GetTokenValue("expires_at") is { } text
            ? DateTimeOffset.Parse(text, CultureInfo.InvariantCulture)
            : DateTimeOffset.MinValue;
        if (access is not null && expiresAt - clock.GetUtcNow() > options.Value.RefreshBefore)
        {
            return (access, false);
        }

        if (properties.GetTokenValue("refresh_token") is not { } refresh)
        {
            return (null, false);
        }

        var settings = oidc.Get(OpenIdConnectDefaults.AuthenticationScheme);
        var configuration = await settings.ConfigurationManager!.GetConfigurationAsync(cancellationToken);
        using var response = await http.CreateClient(HttpClientName).PostAsync(
            configuration.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refresh,
                ["client_id"] = settings.ClientId!,
                ["client_secret"] = settings.ClientSecret ?? string.Empty,
            }),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            // The session ended at Keycloak, or the refresh token expired: this session is over.
            // Keycloak's answer is an error code and description, never a token.
            logger.LogWarning("Keycloak refused to renew a session: {Status} {Error}", (int)response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            return (null, false);
        }

        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var root = json.RootElement;
        var newAccess = root.GetProperty("access_token").GetString()!;
        var expiresIn = root.GetProperty("expires_in").GetInt32();

        properties.UpdateTokenValue("access_token", newAccess);
        properties.UpdateTokenValue("expires_at", clock.GetUtcNow().AddSeconds(expiresIn).ToString("o", CultureInfo.InvariantCulture));
        if (root.TryGetProperty("refresh_token", out var newRefresh))
        {
            properties.UpdateTokenValue("refresh_token", newRefresh.GetString()!);
        }

        if (root.TryGetProperty("id_token", out var idToken))
        {
            properties.UpdateTokenValue("id_token", idToken.GetString()!);
        }

        return (newAccess, true);
    }
}
