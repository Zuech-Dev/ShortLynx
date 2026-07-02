using System.Net;
using System.Text;
using System.Text.Json;
using ShortLynx.Services.Social;

namespace ShortLynx.Tests.Services.Social;

public class ConnectorPublishTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;
        public string? LastRequestBody;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static SocialConnectionContext BlueskyCtx(string? refresh = "refresh-1") => new(
        "did:plc:abc", "me.bsky.social", null, new SocialTokens("access-1", refresh));

    private static SocialConnectionContext MastodonCtx() => new(
        "mastodon.social#1", "@me@mastodon.social", "https://mastodon.social", new SocialTokens("token-1", null));

    // ── Bluesky ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Bluesky_Publish_CreatesRecord_WithLinkFacet_AndReturnsPostUrl()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"uri":"at://did:plc:abc/app.bsky.feed.post/3kxyz","cid":"bafy..."}
            """);
        var connector = new BlueskyConnector(new HttpClient(handler));

        const string text = "Check this https://shrtlynx.com/abc out";
        var post = await connector.PublishAsync(BlueskyCtx(), text);

        Assert.Equal("at://did:plc:abc/app.bsky.feed.post/3kxyz", post.ExternalPostId);
        Assert.Equal("https://bsky.app/profile/me.bsky.social/post/3kxyz", post.PostUrl);

        // The URL must be a byte-offset link facet, or it renders as unclickable text on Bluesky.
        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var facet = doc.RootElement.GetProperty("record").GetProperty("facets")[0];
        Assert.Equal(Encoding.UTF8.GetByteCount("Check this "), facet.GetProperty("index").GetProperty("byteStart").GetInt32());
        Assert.Equal(Encoding.UTF8.GetByteCount("Check this https://shrtlynx.com/abc"), facet.GetProperty("index").GetProperty("byteEnd").GetInt32());
        Assert.Equal("https://shrtlynx.com/abc",
            facet.GetProperty("features")[0].GetProperty("uri").GetString());
        Assert.Equal("did:plc:abc", doc.RootElement.GetProperty("repo").GetString());
    }

    [Fact]
    public async Task Bluesky_Publish_NonAsciiText_UsesByteOffsets_NotCharOffsets()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"uri":"at://did:plc:abc/app.bsky.feed.post/1","cid":"c"}
            """);
        var connector = new BlueskyConnector(new HttpClient(handler));

        const string text = "héllo https://a.io"; // 'é' is 2 UTF-8 bytes → byteStart 7, not char index 6
        await connector.PublishAsync(BlueskyCtx(), text);

        using var doc = JsonDocument.Parse(handler.LastRequestBody!);
        var index = doc.RootElement.GetProperty("record").GetProperty("facets")[0].GetProperty("index");
        Assert.Equal(7, index.GetProperty("byteStart").GetInt32());
    }

    [Fact]
    public async Task Bluesky_Publish_ExpiredToken_ThrowsTokenExpired()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """
            {"error":"ExpiredToken","message":"Token has expired"}
            """);
        var connector = new BlueskyConnector(new HttpClient(handler));

        await Assert.ThrowsAsync<TokenExpiredException>(
            () => connector.PublishAsync(BlueskyCtx(), "hi"));
    }

    [Fact]
    public async Task Bluesky_Publish_OverLimit_Throws_WithoutNetworkCall()
    {
        var handler = new StubHandler(HttpStatusCode.OK, "{}");
        var connector = new BlueskyConnector(new HttpClient(handler));

        await Assert.ThrowsAsync<ArgumentException>(
            () => connector.PublishAsync(BlueskyCtx(), new string('x', 301)));
        Assert.Null(handler.LastRequest);
    }

    [Fact]
    public async Task Bluesky_Refresh_ReturnsRotatedTokens()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"did":"did:plc:abc","handle":"me.bsky.social","accessJwt":"access-2","refreshJwt":"refresh-2"}
            """);
        var connector = new BlueskyConnector(new HttpClient(handler));

        var tokens = await connector.RefreshAsync(BlueskyCtx());

        Assert.Equal("access-2", tokens!.AccessToken);
        Assert.Equal("refresh-2", tokens.RefreshToken);
        // Refresh authenticates with the REFRESH token, not the stale access token.
        Assert.Equal("refresh-1", handler.LastRequest!.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Bluesky_Refresh_DeadRefreshToken_ReturnsNull()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """{"error":"ExpiredToken"}""");
        var connector = new BlueskyConnector(new HttpClient(handler));

        Assert.Null(await connector.RefreshAsync(BlueskyCtx()));
    }

    // ── Mastodon ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Mastodon_Publish_PostsStatus_AndReturnsUrl()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"id":"111222","url":"https://mastodon.social/@me/111222"}
            """);
        var connector = new MastodonConnector(new HttpClient(handler));

        var post = await connector.PublishAsync(MastodonCtx(), "hello https://shrtlynx.com/abc");

        Assert.Equal("111222", post.ExternalPostId);
        Assert.Equal("https://mastodon.social/@me/111222", post.PostUrl);
        Assert.Equal("https://mastodon.social/api/v1/statuses", handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("shrtlynx.com/abc", handler.LastRequestBody);
    }

    [Fact]
    public async Task Mastodon_Publish_InvalidToken_ThrowsTokenExpired()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{"error":"The access token is invalid"}""");
        var connector = new MastodonConnector(new HttpClient(handler));

        await Assert.ThrowsAsync<TokenExpiredException>(
            () => connector.PublishAsync(MastodonCtx(), "hi"));
    }

    [Fact]
    public async Task Mastodon_Refresh_NotSupported_ReturnsNull()
    {
        var connector = new MastodonConnector(new HttpClient(new StubHandler(HttpStatusCode.OK, "{}")));
        Assert.Null(await connector.RefreshAsync(MastodonCtx()));
    }
}
