using System.Net;
using System.Text;
using ShortLynx.Services.Social;

namespace ShortLynx.Tests.Services.Social;

public class ConnectorMetricsTests
{
    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static SocialConnectionContext BlueskyCtx() => new(
        "did:plc:abc", "me.bsky.social", null, new SocialTokens("access-1", "refresh-1"));

    private static SocialConnectionContext MastodonCtx() => new(
        "mastodon.social#1", "@me@mastodon.social", "https://mastodon.social", new SocialTokens("token-1", null));

    [Fact]
    public async Task Bluesky_Metrics_MapsCounts_QuotesFoldIntoReposts_NoImpressions()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"posts":[{"uri":"at://did:plc:abc/app.bsky.feed.post/rk1","likeCount":12,"repostCount":3,"replyCount":2,"quoteCount":1}]}
            """);
        var connector = new BlueskyConnector(new HttpClient(handler));

        var metrics = await connector.GetPostMetricsAsync(BlueskyCtx(), "at://did:plc:abc/app.bsky.feed.post/rk1");

        Assert.Equal(12, metrics!.Likes);
        Assert.Equal(4, metrics.Reposts);   // 3 reposts + 1 quote
        Assert.Equal(2, metrics.Replies);
        Assert.Null(metrics.Impressions);   // Bluesky's public API exposes no view counts

        // The at:// URI must be escaped into the query string.
        Assert.Contains("uris=at%3A%2F%2F", handler.LastRequest!.RequestUri!.Query);
    }

    [Fact]
    public async Task Bluesky_Metrics_PostDeleted_ReturnsNull()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """{"posts":[]}""");
        var connector = new BlueskyConnector(new HttpClient(handler));

        Assert.Null(await connector.GetPostMetricsAsync(BlueskyCtx(), "at://gone"));
    }

    [Fact]
    public async Task Bluesky_Metrics_ExpiredToken_ThrowsTokenExpired()
    {
        var handler = new StubHandler(HttpStatusCode.BadRequest, """{"error":"ExpiredToken"}""");
        var connector = new BlueskyConnector(new HttpClient(handler));

        await Assert.ThrowsAsync<TokenExpiredException>(
            () => connector.GetPostMetricsAsync(BlueskyCtx(), "at://x"));
    }

    [Fact]
    public async Task Mastodon_Metrics_MapsSnakeCaseCounts()
    {
        var handler = new StubHandler(HttpStatusCode.OK, """
            {"id":"111222","favourites_count":7,"reblogs_count":2,"replies_count":1}
            """);
        var connector = new MastodonConnector(new HttpClient(handler));

        var metrics = await connector.GetPostMetricsAsync(MastodonCtx(), "111222");

        Assert.Equal(7, metrics!.Likes);
        Assert.Equal(2, metrics.Reposts);
        Assert.Equal(1, metrics.Replies);
        Assert.Null(metrics.Impressions);
        Assert.Equal("https://mastodon.social/api/v1/statuses/111222",
            handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Mastodon_Metrics_PostDeleted_ReturnsNull()
    {
        var handler = new StubHandler(HttpStatusCode.NotFound, """{"error":"Record not found"}""");
        var connector = new MastodonConnector(new HttpClient(handler));

        Assert.Null(await connector.GetPostMetricsAsync(MastodonCtx(), "gone"));
    }

    [Fact]
    public async Task Mastodon_Metrics_InvalidToken_ThrowsTokenExpired()
    {
        var handler = new StubHandler(HttpStatusCode.Unauthorized, """{"error":"invalid"}""");
        var connector = new MastodonConnector(new HttpClient(handler));

        await Assert.ThrowsAsync<TokenExpiredException>(
            () => connector.GetPostMetricsAsync(MastodonCtx(), "111"));
    }
}
