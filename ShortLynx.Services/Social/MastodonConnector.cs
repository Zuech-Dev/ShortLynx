using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Social;

/// <summary>
/// Mastodon connector. Mastodon is federated (per-instance), so the user supplies their instance URL
/// plus an access token (created under Settings → Development on their instance). Connect = verify the
/// token via <c>/api/v1/accounts/verify_credentials</c>. Typed HttpClient, registered via AddHttpClient.
/// </summary>
public sealed class MastodonConnector(HttpClient http) : ISocialConnector
{
    public SocialPlatform Platform => SocialPlatform.Mastodon;

    public async Task<SocialIdentity> ConnectAsync(SocialCredentials credentials, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(credentials.InstanceUrl))
            throw new ArgumentException("Mastodon needs your instance URL (e.g. https://mastodon.social).");

        var baseUrl = credentials.InstanceUrl.Trim().TrimEnd('/');
        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            baseUrl = $"https://{baseUrl}";

        using var request = new HttpRequestMessage(HttpMethod.Get, $"{baseUrl}/api/v1/accounts/verify_credentials");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.Secret);

        using var response = await http.SendAsync(request, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new ArgumentException("Mastodon rejected the access token.");

        response.EnsureSuccessStatusCode();

        var account = await response.Content.ReadFromJsonAsync<VerifyCredentialsResponse>(ct)
                      ?? throw new InvalidOperationException("Empty verify_credentials response from Mastodon.");

        // Mastodon account ids are only unique per instance, so qualify the external id with the host —
        // otherwise user 12345 on two different instances would collide in the upsert key.
        var host = new Uri(baseUrl).Host;
        return new SocialIdentity(
            ExternalAccountId: $"{host}#{account.Id}",
            Handle: $"@{account.Username}@{host}",
            AccessToken: credentials.Secret,   // Mastodon user tokens are long-lived; no refresh token.
            RefreshToken: null,
            ExpiresAt: null);
    }

    private sealed record VerifyCredentialsResponse(string Id, string Username, string Acct);
}
