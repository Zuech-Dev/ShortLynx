using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Social;

/// <summary>
/// Reddit connector. OAuth-only (like Threads): the dashboard's authorize/callback endpoints drive the
/// connect flow; <see cref="ConnectAsync"/> always rejects. Posts are published as self (text) posts to
/// the user's own profile (subreddit <c>u_{username}</c>) — posting into arbitrary subreddits is
/// deliberately out of scope for v1 (per-subreddit rules make automated posting there an easy way to
/// get an account banned).
///
/// API shape: token endpoints live on <c>www.reddit.com</c> and authenticate with HTTP Basic
/// (client_id:secret); resource endpoints live on <c>oauth.reddit.com</c> with the Bearer token. Reddit
/// REQUIRES a descriptive User-Agent and blocks default library values. Submit responses report errors
/// in a <c>json.errors</c> array even with HTTP 200, so success is judged on that, not the status code.
/// </summary>
public sealed class RedditConnector : IOAuthSocialConnector
{
    private const string AuthHost = "https://www.reddit.com";
    private const string ApiHost = "https://oauth.reddit.com";

    private readonly HttpClient _http;
    private readonly IOptions<RedditOptions> _options;

    public RedditConnector(HttpClient http, IOptions<RedditOptions> options)
    {
        _http = http;
        _options = options;
        // Reddit rejects requests without a descriptive UA (429/403s). Set once for every call.
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.Value.UserAgent);
    }

    private RedditOptions Options => _options.Value;

    public SocialPlatform Platform => SocialPlatform.Reddit;

    public Task<SocialIdentity> ConnectAsync(SocialCredentials credentials, CancellationToken ct = default)
        => throw new ArgumentException(
            "Reddit accounts must be connected via the ShortLynx dashboard's OAuth flow, not the API.");

    public string BuildAuthorizeUrl(string redirectUri, string state)
        => $"{AuthHost}/api/v1/authorize" +
           $"?client_id={Uri.EscapeDataString(Options.AppId)}" +
           $"&response_type=code" +
           $"&state={Uri.EscapeDataString(state)}" +
           $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
           $"&duration=permanent" + // permanent → a refresh token comes back with the access token
           $"&scope={Uri.EscapeDataString("identity submit read")}";

    public async Task<SocialIdentity> ExchangeAuthorizationCodeAsync(string code, string redirectUri, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{AuthHost}/api/v1/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
            }),
        };
        request.Headers.Authorization = BasicAuth();

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
            throw new ArgumentException($"Reddit rejected the authorization code: {await response.Content.ReadAsStringAsync(ct)}");

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct)
                    ?? throw new InvalidOperationException("Empty access_token response from Reddit.");
        if (string.IsNullOrEmpty(token.AccessToken))
            throw new ArgumentException($"Reddit rejected the authorization code: {token.Error ?? "no access token returned"}");

        // Identity for the stable external id + display handle.
        using var meRequest = new HttpRequestMessage(HttpMethod.Get, $"{ApiHost}/api/v1/me");
        meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var meResponse = await _http.SendAsync(meRequest, ct);
        meResponse.EnsureSuccessStatusCode();

        var me = await meResponse.Content.ReadFromJsonAsync<MeResponse>(ct)
                 ?? throw new InvalidOperationException("Empty identity response from Reddit.");

        return new SocialIdentity(
            ExternalAccountId: me.Id,
            Handle: $"u/{me.Name}",
            AccessToken: token.AccessToken,
            RefreshToken: token.RefreshToken,
            ExpiresAt: DateTimeOffset.UtcNow.AddSeconds(token.ExpiresIn));
    }

    public async Task<SocialPostRef> PublishAsync(SocialConnectionContext connection, string text, CancellationToken ct = default)
    {
        // Self post to the user's own profile: subreddit "u_{username}". The handle is stored as
        // "u/{username}" at connect time.
        var username = connection.Handle.StartsWith("u/", StringComparison.Ordinal)
            ? connection.Handle[2..]
            : connection.Handle;

        // Reddit posts are (title, body); our composed text becomes both — title is the first line,
        // clamped to Reddit's 300-char title limit, and the full text (short URL included) is the body.
        var title = text.Split('\n', 2)[0];
        if (title.Length > 300) title = title[..297] + "…";

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{ApiHost}/api/submit")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["api_type"] = "json",
                ["kind"] = "self",
                ["sr"] = $"u_{username}",
                ["title"] = title,
                ["text"] = text,
            }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Tokens.AccessToken);

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TokenExpiredException("Reddit access token expired.");
        response.EnsureSuccessStatusCode();

        var submit = await response.Content.ReadFromJsonAsync<SubmitEnvelope>(ct)
                     ?? throw new InvalidOperationException("Empty submit response from Reddit.");

        // Reddit reports validation failures inside json.errors with HTTP 200.
        if (submit.Json?.Errors is { Count: > 0 } errors)
            throw new ArgumentException($"Reddit rejected the post: {string.Join("; ", errors.Select(e => string.Join(" ", e)))}");
        if (submit.Json?.Data?.Name is not { } fullname)
            throw new InvalidOperationException("Reddit submit response carried no post id.");

        return new SocialPostRef(fullname, submit.Json.Data.Url);
    }

    public async Task<SocialTokens?> RefreshAsync(SocialConnectionContext connection, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(connection.Tokens.RefreshToken)) return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{AuthHost}/api/v1/access_token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = connection.Tokens.RefreshToken,
            }),
        };
        request.Headers.Authorization = BasicAuth();

        using var response = await _http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null; // refresh token revoked/expired → re-auth needed

        var token = await response.Content.ReadFromJsonAsync<TokenResponse>(ct);
        if (token is null || string.IsNullOrEmpty(token.AccessToken)) return null;

        // Reddit usually keeps the same refresh token; fall back to the current one when absent.
        return new SocialTokens(token.AccessToken, token.RefreshToken ?? connection.Tokens.RefreshToken);
    }

    public async Task<SocialPostMetrics?> GetPostMetricsAsync(
        SocialConnectionContext connection, string externalPostId, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"{ApiHost}/api/info?id={Uri.EscapeDataString(externalPostId)}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Tokens.AccessToken);

        using var response = await _http.SendAsync(request, ct);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new TokenExpiredException("Reddit access token expired.");
        response.EnsureSuccessStatusCode();

        var info = await response.Content.ReadFromJsonAsync<InfoEnvelope>(ct);
        var post = info?.Data?.Children?.FirstOrDefault()?.Data;
        if (post is null) return null; // deleted on-platform

        // Reddit exposes score + comment count to app users, not impressions/views.
        return new SocialPostMetrics(
            Impressions: null,
            Likes: post.Score,
            Reposts: null,
            Replies: post.NumComments);
    }

    private AuthenticationHeaderValue BasicAuth() => new("Basic",
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Options.AppId}:{Options.AppSecret}")));

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("refresh_token")] string? RefreshToken,
        [property: JsonPropertyName("expires_in")] long ExpiresIn,
        [property: JsonPropertyName("error")] string? Error);
    private sealed record MeResponse(string Id, string Name);
    private sealed record SubmitEnvelope(SubmitJson? Json);
    private sealed record SubmitJson(List<List<string>>? Errors, SubmitData? Data);
    private sealed record SubmitData(string? Url, string? Name);
    private sealed record InfoEnvelope(InfoData? Data);
    private sealed record InfoData(List<InfoChild>? Children);
    private sealed record InfoChild(InfoPost? Data);
    private sealed record InfoPost(
        long? Score,
        [property: JsonPropertyName("num_comments")] long? NumComments);
}
