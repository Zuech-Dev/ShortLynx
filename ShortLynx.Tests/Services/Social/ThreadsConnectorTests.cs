using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using ShortLynx.Services.Social;

namespace ShortLynx.Tests.Services.Social;

public class ThreadsConnectorTests
{
    // Queues canned responses in call order — the OAuth exchange and publish flows are multi-request
    // chains, so a single stub-per-test doesn't work here.
    private sealed class QueuedHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();
        public readonly List<HttpRequestMessage> Requests = [];
        public readonly List<string?> RequestBodies = [];

        public QueuedHandler Enqueue(HttpStatusCode status, string body)
        {
            _responses.Enqueue((status, body));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
            var (status, body) = _responses.Count > 0 ? _responses.Dequeue() : (HttpStatusCode.OK, "{}");
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }

    private static ThreadsConnector Make(QueuedHandler handler, string appSecret = "test-app-secret")
        => new(new HttpClient(handler), Options.Create(new MetaOptions
        {
            AppId = "test-app-id", AppSecret = appSecret, RedirectUri = "https://shortlynx.dev/social/threads/callback",
        }));

    private static SocialConnectionContext Ctx(string token = "long-lived-token") => new(
        "17800000000000000", "@me", null, new SocialTokens(token, null));

    [Fact]
    public void BuildAuthorizeUrl_IncludesClientIdScopeAndState()
    {
        var connector = Make(new QueuedHandler());

        var url = connector.BuildAuthorizeUrl("https://shortlynx.dev/social/threads/callback", "abc123");

        Assert.StartsWith("https://threads.net/oauth/authorize", url);
        Assert.Contains("client_id=test-app-id", url);
        Assert.Contains("state=abc123", url);
        Assert.Contains("threads_content_publish", url);
        Assert.Contains($"redirect_uri={Uri.EscapeDataString("https://shortlynx.dev/social/threads/callback")}", url);
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_ChainsThreeCalls_ReturnsLongLivedIdentity()
    {
        var handler = new QueuedHandler()
            .Enqueue(HttpStatusCode.OK, """{"access_token":"short-lived","user_id":17800000000000000}""")
            .Enqueue(HttpStatusCode.OK, """{"access_token":"long-lived-token","token_type":"bearer","expires_in":5184000}""")
            .Enqueue(HttpStatusCode.OK, """{"id":"17800000000000000","username":"anthony"}""");
        var connector = Make(handler);

        var identity = await connector.ExchangeAuthorizationCodeAsync("auth-code", "https://shortlynx.dev/social/threads/callback");

        Assert.Equal("17800000000000000", identity.ExternalAccountId);
        Assert.Equal("@anthony", identity.Handle);
        Assert.Equal("long-lived-token", identity.AccessToken);
        Assert.Null(identity.RefreshToken); // Threads has no separate refresh token
        Assert.True(identity.ExpiresAt > DateTimeOffset.UtcNow.AddDays(59));

        // Step order matters: code exchange, then long-lived exchange, then profile fetch.
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal("https://graph.threads.net/oauth/access_token", handler.Requests[0].RequestUri!.GetLeftPart(UriPartial.Path));
        Assert.Contains("th_exchange_token", handler.Requests[1].RequestUri!.Query);
        Assert.Contains("/v1.0/me", handler.Requests[2].RequestUri!.AbsolutePath);
        Assert.Contains("code=auth-code", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_RejectedCode_ThrowsArgumentException()
    {
        var handler = new QueuedHandler().Enqueue(HttpStatusCode.BadRequest, """{"error":"invalid_grant"}""");
        var connector = Make(handler);

        await Assert.ThrowsAsync<ArgumentException>(
            () => connector.ExchangeAuthorizationCodeAsync("bad-code", "https://shortlynx.dev/social/threads/callback"));
    }

    [Fact]
    public async Task Publish_ChainsContainerThenPublishThenPermalink_SignsWithAppSecretProof()
    {
        var handler = new QueuedHandler()
            .Enqueue(HttpStatusCode.OK, """{"id":"creation-1"}""")
            .Enqueue(HttpStatusCode.OK, """{"id":"media-1"}""")
            .Enqueue(HttpStatusCode.OK, """{"id":"media-1","permalink":"https://www.threads.net/@me/post/media-1"}""");
        var connector = Make(handler);

        var post = await connector.PublishAsync(Ctx(), "Check this out https://shrtlynx.com/abc");

        Assert.Equal("media-1", post.ExternalPostId);
        Assert.Equal("https://www.threads.net/@me/post/media-1", post.PostUrl);

        Assert.Equal(3, handler.Requests.Count);
        Assert.Contains("/17800000000000000/threads", handler.Requests[0].RequestUri!.AbsolutePath);
        Assert.EndsWith("/threads_publish", handler.Requests[1].RequestUri!.AbsolutePath);
        Assert.Contains("creation_id=creation-1", handler.RequestBodies[1]);
        // Every authenticated call is signed — a bare access_token replay wouldn't carry the app secret.
        Assert.Contains("appsecret_proof=", handler.RequestBodies[0]);
    }

    [Fact]
    public async Task Publish_PermalinkFetchFails_StillReturnsPostRef()
    {
        var handler = new QueuedHandler()
            .Enqueue(HttpStatusCode.OK, """{"id":"creation-1"}""")
            .Enqueue(HttpStatusCode.OK, """{"id":"media-1"}""")
            .Enqueue(HttpStatusCode.InternalServerError, "{}");
        var connector = Make(handler);

        var post = await connector.PublishAsync(Ctx(), "hi");

        Assert.Equal("media-1", post.ExternalPostId);
        Assert.Null(post.PostUrl); // best-effort — publishing itself still succeeded
    }

    [Fact]
    public async Task Publish_OverLimit_ThrowsWithoutNetworkCall()
    {
        var handler = new QueuedHandler();
        var connector = Make(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => connector.PublishAsync(Ctx(), new string('x', 501)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Publish_ExpiredTokenErrorCode190_ThrowsTokenExpired()
    {
        var handler = new QueuedHandler().Enqueue(HttpStatusCode.BadRequest,
            """{"error":{"message":"Error validating access token","type":"OAuthException","code":190}}""");
        var connector = Make(handler);

        await Assert.ThrowsAsync<TokenExpiredException>(() => connector.PublishAsync(Ctx(), "hi"));
    }

    [Fact]
    public async Task Refresh_ReturnsNewAccessToken_NoRefreshToken()
    {
        var handler = new QueuedHandler().Enqueue(HttpStatusCode.OK,
            """{"access_token":"refreshed-token","token_type":"bearer","expires_in":5184000}""");
        var connector = Make(handler);

        var tokens = await connector.RefreshAsync(Ctx());

        Assert.Equal("refreshed-token", tokens!.AccessToken);
        Assert.Null(tokens.RefreshToken);
        Assert.Contains("th_refresh_token", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task Refresh_Failure_ReturnsNull()
    {
        var handler = new QueuedHandler().Enqueue(HttpStatusCode.BadRequest, "{}");
        var connector = Make(handler);

        Assert.Null(await connector.RefreshAsync(Ctx()));
    }

    [Fact]
    public async Task Metrics_ParsesTotalValueShape_QuotesFoldIntoReposts()
    {
        var handler = new QueuedHandler().Enqueue(HttpStatusCode.OK, """
            {"data":[
                {"name":"views","period":"lifetime","total_value":{"value":900}},
                {"name":"likes","period":"lifetime","total_value":{"value":40}},
                {"name":"replies","period":"lifetime","total_value":{"value":5}},
                {"name":"reposts","period":"lifetime","total_value":{"value":3}},
                {"name":"quotes","period":"lifetime","total_value":{"value":1}}
            ]}
            """);
        var connector = Make(handler);

        var metrics = await connector.GetPostMetricsAsync(Ctx(), "media-1");

        Assert.Equal(900, metrics!.Impressions); // Threads DOES report views — unlike Bluesky/Mastodon
        Assert.Equal(40, metrics.Likes);
        Assert.Equal(5, metrics.Replies);
        Assert.Equal(4, metrics.Reposts); // 3 reposts + 1 quote
    }

    [Fact]
    public async Task Metrics_ParsesValuesArrayShape()
    {
        var handler = new QueuedHandler().Enqueue(HttpStatusCode.OK, """
            {"data":[{"name":"views","period":"day","values":[{"value":10},{"value":25}]}]}
            """);
        var connector = Make(handler);

        var metrics = await connector.GetPostMetricsAsync(Ctx(), "media-1");

        Assert.Equal(25, metrics!.Impressions); // last entry in the series
    }

    [Fact]
    public async Task Metrics_PostDeleted_ReturnsNull()
    {
        var handler = new QueuedHandler().Enqueue(HttpStatusCode.NotFound, "{}");
        var connector = Make(handler);

        Assert.Null(await connector.GetPostMetricsAsync(Ctx(), "gone"));
    }

    [Fact]
    public async Task Metrics_ExpiredToken_ThrowsTokenExpired()
    {
        var handler = new QueuedHandler().Enqueue(HttpStatusCode.Unauthorized, "{}");
        var connector = Make(handler);

        await Assert.ThrowsAsync<TokenExpiredException>(() => connector.GetPostMetricsAsync(Ctx(), "media-1"));
    }

    [Fact]
    public async Task ConnectAsync_AlwaysRejects_DirectsToOAuthFlow()
    {
        var connector = Make(new QueuedHandler());

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => connector.ConnectAsync(new SocialCredentials("x", "y")));
        Assert.Contains("OAuth", ex.Message);
    }
}
