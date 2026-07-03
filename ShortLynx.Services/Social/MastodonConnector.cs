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

    public async Task<SocialPostRef> PublishAsync(SocialConnectionContext connection, string text, CancellationToken ct = default)
    {
        if (text.Length > 500)
            throw new ArgumentException("Mastodon posts are limited to 500 characters on most instances.");
        if (string.IsNullOrWhiteSpace(connection.InstanceUrl))
            throw new ArgumentException("The Mastodon connection has no instance URL.");

        var baseUrl = connection.InstanceUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/v1/statuses");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Tokens.AccessToken);
        request.Content = JsonContent.Create(new { status = text });

        using var response = await http.SendAsync(request, ct);

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TokenExpiredException("Mastodon rejected the access token — reconnect the account.");
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity)
            throw new ArgumentException($"Mastodon rejected the post: {await response.Content.ReadAsStringAsync(ct)}");
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<StatusResponse>(ct)
                     ?? throw new InvalidOperationException("Empty status response from Mastodon.");
        return new SocialPostRef(status.Id, status.Url);
    }

    // Mastodon user tokens are long-lived and have no refresh flow — an invalid token means reconnect.
    public Task<SocialTokens?> RefreshAsync(SocialConnectionContext connection, CancellationToken ct = default)
        => Task.FromResult<SocialTokens?>(null);

    public async Task<SocialPostMetrics?> GetPostMetricsAsync(
        SocialConnectionContext connection, string externalPostId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(connection.InstanceUrl))
            throw new ArgumentException("The Mastodon connection has no instance URL.");

        var baseUrl = connection.InstanceUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{baseUrl}/api/v1/statuses/{Uri.EscapeDataString(externalPostId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Tokens.AccessToken);

        using var response = await http.SendAsync(request, ct);

        if (response.StatusCode == HttpStatusCode.NotFound) return null; // deleted on-platform
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TokenExpiredException("Mastodon rejected the access token — reconnect the account.");
        response.EnsureSuccessStatusCode();

        var status = await response.Content.ReadFromJsonAsync<StatusMetricsResponse>(ct);
        if (status is null) return null;

        // Mastodon exposes no view/impression counts — that field stays null here.
        return new SocialPostMetrics(
            Impressions: null,
            Likes: status.FavouritesCount,
            Reposts: status.ReblogsCount,
            Replies: status.RepliesCount);
    }

    private sealed record VerifyCredentialsResponse(string Id, string Username, string Acct);
    private sealed record StatusResponse(string Id, string? Url);

    // Mastodon serializes snake_case; map explicitly since the default binder only handles camelCase.
    private sealed record StatusMetricsResponse(
        [property: System.Text.Json.Serialization.JsonPropertyName("favourites_count")] long? FavouritesCount,
        [property: System.Text.Json.Serialization.JsonPropertyName("reblogs_count")] long? ReblogsCount,
        [property: System.Text.Json.Serialization.JsonPropertyName("replies_count")] long? RepliesCount);
}
