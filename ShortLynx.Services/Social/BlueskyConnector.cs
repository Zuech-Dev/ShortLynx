using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Social;

/// <summary>
/// Bluesky (AT Protocol) connector. Connect = exchange handle + app password for a session via
/// <c>com.atproto.server.createSession</c>, which validates the credentials and yields the account's
/// stable DID, current handle, and access/refresh JWTs. Typed HttpClient — registered via
/// <c>AddHttpClient</c> in the composition root.
/// </summary>
public sealed class BlueskyConnector(HttpClient http) : ISocialConnector
{
    /// <summary>The default PDS host; self-hosted PDS users can override via InstanceUrl.</summary>
    public const string DefaultService = "https://bsky.social";

    public SocialPlatform Platform => SocialPlatform.Bluesky;

    public async Task<SocialIdentity> ConnectAsync(SocialCredentials credentials, CancellationToken ct = default)
    {
        var baseUrl = string.IsNullOrWhiteSpace(credentials.InstanceUrl)
            ? DefaultService
            : credentials.InstanceUrl.TrimEnd('/');

        using var response = await http.PostAsJsonAsync(
            $"{baseUrl}/xrpc/com.atproto.server.createSession",
            new { identifier = credentials.Identifier, password = credentials.Secret },
            ct);

        // Bluesky answers 400/401 for a bad identifier or app password — a user error, not a fault.
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            throw new ArgumentException("Bluesky rejected the handle or app password.");

        response.EnsureSuccessStatusCode();

        var session = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(ct)
                      ?? throw new InvalidOperationException("Empty createSession response from Bluesky.");

        // The DID is the stable id (survives handle changes); the handle is for display.
        return new SocialIdentity(session.Did, session.Handle, session.AccessJwt, session.RefreshJwt, ExpiresAt: null);
    }

    public async Task<SocialPostRef> PublishAsync(SocialConnectionContext connection, string text, CancellationToken ct = default)
    {
        if (text.Length > 300)
            throw new ArgumentException("Bluesky posts are limited to 300 characters.");

        var baseUrl = BaseUrl(connection.InstanceUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/xrpc/com.atproto.repo.createRecord");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Tokens.AccessToken);
        request.Content = JsonContent.Create(new
        {
            repo = connection.ExternalAccountId, // the DID
            collection = "app.bsky.feed.post",
            record = new
            {
                text,
                createdAt = DateTimeOffset.UtcNow.ToString("O"),
                facets = LinkFacets(text),
            },
        });

        using var response = await http.SendAsync(request, ct);
        await ThrowIfAuthProblemAsync(response, ct);
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<CreateRecordResponse>(ct)
                      ?? throw new InvalidOperationException("Empty createRecord response from Bluesky.");

        // at://did/app.bsky.feed.post/{rkey} → a human-viewable bsky.app URL.
        var rkey = created.Uri[(created.Uri.LastIndexOf('/') + 1)..];
        return new SocialPostRef(created.Uri, $"https://bsky.app/profile/{connection.Handle}/post/{rkey}");
    }

    public async Task<SocialTokens?> RefreshAsync(SocialConnectionContext connection, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(connection.Tokens.RefreshToken)) return null;

        var baseUrl = BaseUrl(connection.InstanceUrl);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/xrpc/com.atproto.server.refreshSession");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.Tokens.RefreshToken);

        using var response = await http.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null; // refresh token dead → caller surfaces re-auth

        var session = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(ct);
        return session is null ? null : new SocialTokens(session.AccessJwt, session.RefreshJwt);
    }

    private static string BaseUrl(string? instanceUrl)
        => string.IsNullOrWhiteSpace(instanceUrl) ? DefaultService : instanceUrl.TrimEnd('/');

    // Bluesky doesn't auto-link URLs — a post needs an explicit link facet with UTF-8 *byte* offsets,
    // or the URL renders as dead text and nobody can click through.
    internal static object[] LinkFacets(string text)
    {
        var facets = new List<object>();
        foreach (var word in EnumerateUrls(text))
        {
            var byteStart = Encoding.UTF8.GetByteCount(text[..word.Start]);
            var byteEnd = byteStart + Encoding.UTF8.GetByteCount(text.Substring(word.Start, word.Length));
            facets.Add(new
            {
                index = new { byteStart, byteEnd },
                features = new object[] { new Dictionary<string, object> { ["$type"] = "app.bsky.richtext.facet#link", ["uri"] = text.Substring(word.Start, word.Length) } },
            });
        }
        return facets.ToArray();
    }

    private static IEnumerable<(int Start, int Length)> EnumerateUrls(string text)
    {
        var index = 0;
        while (index < text.Length)
        {
            var start = text.IndexOf("http", index, StringComparison.OrdinalIgnoreCase);
            if (start < 0) yield break;
            var end = start;
            while (end < text.Length && !char.IsWhiteSpace(text[end])) end++;
            var candidate = text[start..end].TrimEnd('.', ',', ')', '!', '?');
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                yield return (start, candidate.Length);
            index = end;
        }
    }

    // 401 (or XRPC's 400 ExpiredToken) means the access JWT is stale → refresh + retry upstream.
    private static async Task ThrowIfAuthProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new TokenExpiredException("Bluesky access token expired.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            if (body.Contains("ExpiredToken", StringComparison.OrdinalIgnoreCase))
                throw new TokenExpiredException("Bluesky access token expired.");
            throw new ArgumentException($"Bluesky rejected the post: {body}");
        }
    }

    private sealed record CreateSessionResponse(string Did, string Handle, string AccessJwt, string? RefreshJwt);
    private sealed record CreateRecordResponse(string Uri, string Cid);
}
