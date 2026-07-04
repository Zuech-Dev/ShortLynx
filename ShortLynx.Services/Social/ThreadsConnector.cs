using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Social;

/// <summary>
/// Threads (Meta) connector. Unlike Bluesky/Mastodon, Threads is OAuth-only — there are no user-supplied
/// credentials to validate, so <see cref="ConnectAsync"/> always rejects with instructions to use the
/// dashboard's OAuth flow (<see cref="IOAuthSocialConnector"/>) instead.
///
/// API shape: the OAuth token endpoints (<c>graph.threads.net/oauth/...</c>, <c>/access_token</c>,
/// <c>/refresh_access_token</c>) are unversioned; resource endpoints (<c>/v1.0/{id}/...</c>) are
/// versioned. Every authenticated Graph call is signed with <c>appsecret_proof</c> per Meta's
/// recommended security practice.
/// </summary>
public sealed class ThreadsConnector(HttpClient http, IOptions<MetaOptions> options) : IOAuthSocialConnector
{
    private const string AuthorizeHost = "https://threads.net";
    private const string ApiHost = "https://graph.threads.net";

    private MetaOptions Options => options.Value;

    public SocialPlatform Platform => SocialPlatform.Threads;

    public Task<SocialIdentity> ConnectAsync(SocialCredentials credentials, CancellationToken ct = default)
        => throw new ArgumentException(
            "Threads accounts must be connected via the ShortLynx dashboard's OAuth flow, not the API.");

    public string BuildAuthorizeUrl(string redirectUri, string state)
    {
        var scope = Uri.EscapeDataString("threads_basic,threads_content_publish,threads_manage_insights");
        return $"{AuthorizeHost}/oauth/authorize" +
               $"?client_id={Uri.EscapeDataString(Options.AppId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&scope={scope}" +
               $"&response_type=code" +
               $"&state={Uri.EscapeDataString(state)}";
    }

    public async Task<SocialIdentity> ExchangeAuthorizationCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        // Step 1: authorization code → short-lived user access token (also yields the Threads user id).
        using var codeResponse = await http.PostAsync($"{ApiHost}/oauth/access_token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = Options.AppId,
                ["client_secret"] = Options.AppSecret,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri,
                ["code"] = code,
            }), ct);

        if (!codeResponse.IsSuccessStatusCode)
            throw new ArgumentException($"Threads rejected the authorization code: {await codeResponse.Content.ReadAsStringAsync(ct)}");

        var shortLived = await codeResponse.Content.ReadFromJsonAsync<ShortLivedTokenResponse>(ct)
                         ?? throw new InvalidOperationException("Empty access_token response from Threads.");

        // Step 2: short-lived → long-lived token (~60 days).
        using var exchangeResponse = await http.GetAsync(
            $"{ApiHost}/access_token?grant_type=th_exchange_token" +
            $"&client_secret={Uri.EscapeDataString(Options.AppSecret)}" +
            $"&access_token={Uri.EscapeDataString(shortLived.AccessToken)}", ct);
        exchangeResponse.EnsureSuccessStatusCode();

        var longLived = await exchangeResponse.Content.ReadFromJsonAsync<LongLivedTokenResponse>(ct)
                        ?? throw new InvalidOperationException("Empty token-exchange response from Threads.");

        // Step 3: fetch the profile for a display handle (the numeric user id is the stable external id).
        using var profileResponse = await http.GetAsync(
            $"{ApiHost}/v1.0/me?fields=id,username" +
            $"&access_token={Uri.EscapeDataString(longLived.AccessToken)}" +
            $"&appsecret_proof={AppSecretProof(longLived.AccessToken)}", ct);
        profileResponse.EnsureSuccessStatusCode();

        var profile = await profileResponse.Content.ReadFromJsonAsync<ProfileResponse>(ct)
                     ?? throw new InvalidOperationException("Empty profile response from Threads.");

        return new SocialIdentity(
            ExternalAccountId: profile.Id,
            Handle: $"@{profile.Username}",
            AccessToken: longLived.AccessToken,
            RefreshToken: null, // Threads has no separate refresh token — the access token itself is refreshed.
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(longLived.ExpiresIn));
    }

    public async Task<SocialPostRef> PublishAsync(SocialConnectionContext connection, string text, CancellationToken ct = default)
    {
        if (text.Length > 500)
            throw new ArgumentException("Threads posts are limited to 500 characters.");

        var token = connection.Tokens.AccessToken;
        var proof = AppSecretProof(token);

        // Two-step publish: create a container, then publish it.
        using var createResponse = await http.PostAsync(
            $"{ApiHost}/v1.0/{connection.ExternalAccountId}/threads", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["media_type"] = "TEXT",
                    ["text"] = text,
                    ["access_token"] = token,
                    ["appsecret_proof"] = proof,
                }), ct);
        await ThrowIfAuthProblemAsync(createResponse, ct);
        createResponse.EnsureSuccessStatusCode();

        var container = await createResponse.Content.ReadFromJsonAsync<IdResponse>(ct)
                        ?? throw new InvalidOperationException("Empty container response from Threads.");

        using var publishResponse = await http.PostAsync(
            $"{ApiHost}/v1.0/{connection.ExternalAccountId}/threads_publish", new FormUrlEncodedContent(
                new Dictionary<string, string>
                {
                    ["creation_id"] = container.Id,
                    ["access_token"] = token,
                    ["appsecret_proof"] = proof,
                }), ct);
        await ThrowIfAuthProblemAsync(publishResponse, ct);
        publishResponse.EnsureSuccessStatusCode();

        var published = await publishResponse.Content.ReadFromJsonAsync<IdResponse>(ct)
                        ?? throw new InvalidOperationException("Empty publish response from Threads.");

        // Best-effort permalink fetch — publishing already succeeded, so a failure here shouldn't fail
        // the whole operation; the post just won't have a clickable URL in our history.
        string? permalink = null;
        try
        {
            using var mediaResponse = await http.GetAsync(
                $"{ApiHost}/v1.0/{published.Id}?fields=permalink" +
                $"&access_token={Uri.EscapeDataString(token)}&appsecret_proof={proof}", ct);
            if (mediaResponse.IsSuccessStatusCode)
                permalink = (await mediaResponse.Content.ReadFromJsonAsync<PermalinkResponse>(ct))?.Permalink;
        }
        catch (HttpRequestException) { /* non-fatal — the post itself published fine */ }

        return new SocialPostRef(published.Id, permalink);
    }

    public async Task<SocialTokens?> RefreshAsync(SocialConnectionContext connection, CancellationToken ct = default)
    {
        // Threads refreshes the long-lived access token itself — there's no separate refresh token.
        using var response = await http.GetAsync(
            $"{ApiHost}/refresh_access_token?grant_type=th_refresh_token" +
            $"&access_token={Uri.EscapeDataString(connection.Tokens.AccessToken)}", ct);
        if (!response.IsSuccessStatusCode) return null; // token too old/invalid to refresh → re-auth needed

        var refreshed = await response.Content.ReadFromJsonAsync<LongLivedTokenResponse>(ct);
        return refreshed is null ? null : new SocialTokens(refreshed.AccessToken, null);
    }

    public async Task<SocialPostMetrics?> GetPostMetricsAsync(
        SocialConnectionContext connection, string externalPostId, CancellationToken ct = default)
    {
        var token = connection.Tokens.AccessToken;
        using var response = await http.GetAsync(
            $"{ApiHost}/v1.0/{externalPostId}/insights?metric=views,likes,replies,reposts,quotes" +
            $"&access_token={Uri.EscapeDataString(token)}&appsecret_proof={AppSecretProof(token)}", ct);

        if (response.StatusCode == HttpStatusCode.NotFound) return null; // deleted on-platform
        await ThrowIfAuthProblemAsync(response, ct);
        response.EnsureSuccessStatusCode();

        var insights = await response.Content.ReadFromJsonAsync<InsightsResponse>(ct);
        var byName = insights?.Data?.ToDictionary(m => m.Name, m => m.Value(), StringComparer.OrdinalIgnoreCase)
                     ?? [];

        long? Get(string name) => byName.TryGetValue(name, out var v) ? v : null;

        return new SocialPostMetrics(
            Impressions: Get("views"),
            Likes: Get("likes"),
            Reposts: (Get("reposts") ?? 0) + (Get("quotes") ?? 0),
            Replies: Get("replies"));
    }

    // HMAC-SHA256(access_token, app_secret) as lowercase hex — Meta's recommended tamper-proofing for
    // server-side Graph API calls (rejects calls where the token is replayed by someone without the secret).
    private string AppSecretProof(string accessToken)
    {
        var keyBytes = Encoding.UTF8.GetBytes(Options.AppSecret);
        var messageBytes = Encoding.UTF8.GetBytes(accessToken);
        var hash = HMACSHA256.HashData(keyBytes, messageBytes);
        return Convert.ToHexStringLower(hash);
    }

    // Meta reports token problems as HTTP 400 with an OAuthException error type/code 190, not a clean 401.
    private static async Task ThrowIfAuthProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new TokenExpiredException("Threads access token expired.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            try
            {
                var error = JsonSerializer.Deserialize<ErrorEnvelope>(body, CaseInsensitive);
                if (error?.Error is { Code: 190 } or { Type: "OAuthException" })
                    throw new TokenExpiredException("Threads access token expired.");
            }
            catch (JsonException) { /* not the expected error shape — fall through to a generic message */ }
            throw new ArgumentException($"Threads rejected the request: {body}");
        }
    }

    // ReadFromJsonAsync (used everywhere else in this file) is case-insensitive by default; a bare
    // JsonSerializer.Deserialize call is not, so this one needs the option spelled out explicitly.
    private static readonly JsonSerializerOptions CaseInsensitive = new() { PropertyNameCaseInsensitive = true };

    // Threads/Graph API responses are snake_case — property names need explicit JsonPropertyName since
    // case-insensitive matching alone can't bridge "access_token" to "AccessToken" (the underscore).
    private sealed record ShortLivedTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("user_id")] long UserId);
    private sealed record LongLivedTokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("token_type")] string? TokenType,
        [property: JsonPropertyName("expires_in")] long ExpiresIn);
    private sealed record ProfileResponse(string Id, string Username);
    private sealed record IdResponse(string Id);
    private sealed record PermalinkResponse(string Id, string? Permalink);
    private sealed record ErrorEnvelope(ErrorDetail? Error);
    private sealed record ErrorDetail(string? Message, string? Type, int Code);

    // Threads insights values arrive as either a single lifetime total_value or a values[] time series
    // depending on the metric — read whichever is present rather than assuming one shape.
    private sealed record InsightsResponse(List<MetricEntry>? Data);
    private sealed record MetricEntry(
        string Name,
        [property: JsonPropertyName("total_value")] TotalValue? TotalValue,
        List<ValueEntry>? Values)
    {
        public long? Value() => TotalValue?.Value ?? Values?.LastOrDefault()?.Value;
    }
    private sealed record TotalValue(long Value);
    private sealed record ValueEntry(long Value);
}
