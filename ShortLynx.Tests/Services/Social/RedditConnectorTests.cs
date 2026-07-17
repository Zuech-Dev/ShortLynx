using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ShortLynx.Services.Social;

namespace ShortLynx.Tests.Services.Social;

public class RedditConnectorTests
{
    // Scripted multi-response handler: Reddit flows chain calls (token → identity), so each request
    // pops the next canned response; every request is captured for assertions.
    private sealed class SequenceHandler(params (HttpStatusCode Status, string Body)[] responses) : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = [];
        public readonly List<string?> RequestBodies = [];
        private int _index;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content is null ? null : await request.Content.ReadAsStringAsync(ct));
            var (status, body) = responses[Math.Min(_index++, responses.Length - 1)];
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static RedditConnector Make(SequenceHandler handler) => new(
        new HttpClient(handler),
        Options.Create(new RedditOptions
        {
            AppId = "reddit-client-id",
            AppSecret = "reddit-secret",
            RedirectUri = "https://shortlynx.dev/social/reddit/callback",
            UserAgent = "web:shortlynx-tests:v1.0 (by /u/test)",
        }));

    private static SocialConnectionContext Ctx(string? refresh = "refresh-1") => new(
        "t2_abc", "u/testuser", null, new SocialTokens("access-1", refresh));

    [Fact]
    public void BuildAuthorizeUrl_CarriesClientId_State_PermanentDuration_AndScopes()
    {
        var url = Make(new SequenceHandler()).BuildAuthorizeUrl("https://shortlynx.dev/social/reddit/callback", "state123");

        Assert.StartsWith("https://www.reddit.com/api/v1/authorize?", url);
        Assert.Contains("client_id=reddit-client-id", url);
        Assert.Contains("state=state123", url);
        Assert.Contains("duration=permanent", url); // without this Reddit issues no refresh token
        Assert.Contains("scope=identity%20submit%20read", url);
        Assert.Contains(Uri.EscapeDataString("https://shortlynx.dev/social/reddit/callback"), url);
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_UsesBasicAuth_ThenFetchesIdentity()
    {
        var handler = new SequenceHandler(
            (HttpStatusCode.OK, """{"access_token":"access-1","refresh_token":"refresh-1","expires_in":3600}"""),
            (HttpStatusCode.OK, """{"id":"t2_abc","name":"testuser"}"""));
        var connector = Make(handler);

        var identity = await connector.ExchangeAuthorizationCodeAsync("the-code", "https://shortlynx.dev/social/reddit/callback");

        Assert.Equal("t2_abc", identity.ExternalAccountId);
        Assert.Equal("u/testuser", identity.Handle);
        Assert.Equal("access-1", identity.AccessToken);
        Assert.Equal("refresh-1", identity.RefreshToken);
        Assert.NotNull(identity.ExpiresAt);

        // Token endpoint: www host, HTTP Basic with client id:secret, code + redirect_uri in the form.
        var tokenRequest = handler.Requests[0];
        Assert.Equal("https://www.reddit.com/api/v1/access_token", tokenRequest.RequestUri!.ToString());
        Assert.Equal("Basic", tokenRequest.Headers.Authorization!.Scheme);
        Assert.Equal(Convert.ToBase64String(Encoding.UTF8.GetBytes("reddit-client-id:reddit-secret")),
            tokenRequest.Headers.Authorization.Parameter);
        Assert.Contains("grant_type=authorization_code", handler.RequestBodies[0]);
        Assert.Contains("code=the-code", handler.RequestBodies[0]);

        // Identity: oauth host with the Bearer token; Reddit's mandatory custom User-Agent present.
        var meRequest = handler.Requests[1];
        Assert.Equal("https://oauth.reddit.com/api/v1/me", meRequest.RequestUri!.ToString());
        Assert.Equal("access-1", meRequest.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task ExchangeAuthorizationCode_RejectedCode_ThrowsArgumentException()
    {
        var handler = new SequenceHandler((HttpStatusCode.OK, """{"error":"invalid_grant"}"""));
        var connector = Make(handler);

        // Reddit reports a bad code as HTTP 200 with an error field and no access_token.
        await Assert.ThrowsAsync<ArgumentException>(
            () => connector.ExchangeAuthorizationCodeAsync("bad", "https://shortlynx.dev/social/reddit/callback"));
    }

    [Fact]
    public async Task Publish_SubmitsSelfPost_ToOwnProfile_WithClampedTitle()
    {
        var handler = new SequenceHandler((HttpStatusCode.OK, """
            {"json":{"errors":[],"data":{"url":"https://www.reddit.com/user/testuser/comments/1abc/hi/","name":"t3_1abc"}}}
            """));
        var connector = Make(handler);

        var text = "Big launch!\n\nhttps://s.example/abc";
        var post = await connector.PublishAsync(Ctx(), text);

        Assert.Equal("t3_1abc", post.ExternalPostId);
        Assert.Equal("https://www.reddit.com/user/testuser/comments/1abc/hi/", post.PostUrl);

        var body = handler.RequestBodies[0]!;
        Assert.Equal("https://oauth.reddit.com/api/submit", handler.Requests[0].RequestUri!.ToString());
        Assert.Contains("sr=u_testuser", body);      // the user's own profile, not a subreddit
        Assert.Contains("kind=self", body);
        Assert.Contains("title=Big+launch%21", body); // first line becomes the title
        Assert.Contains("api_type=json", body);
    }

    [Fact]
    public async Task Publish_RedditErrorsWithHttp200_ThrowArgumentException()
    {
        var handler = new SequenceHandler((HttpStatusCode.OK, """
            {"json":{"errors":[["RATELIMIT","you are doing that too much","ratelimit"]],"data":null}}
            """));
        var connector = Make(handler);

        // Reddit's submit endpoint reports failures inside json.errors while returning HTTP 200.
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => connector.PublishAsync(Ctx(), "hi"));
        Assert.Contains("RATELIMIT", ex.Message);
    }

    [Fact]
    public async Task Publish_Unauthorized_ThrowsTokenExpired()
    {
        var handler = new SequenceHandler((HttpStatusCode.Unauthorized, "{}"));
        await Assert.ThrowsAsync<TokenExpiredException>(() => Make(handler).PublishAsync(Ctx(), "hi"));
    }

    [Fact]
    public async Task Refresh_ReturnsNewAccessToken_KeepsRefreshTokenWhenNotRotated()
    {
        var handler = new SequenceHandler((HttpStatusCode.OK, """{"access_token":"access-2","expires_in":3600}"""));
        var connector = Make(handler);

        var tokens = await connector.RefreshAsync(Ctx());

        Assert.Equal("access-2", tokens!.AccessToken);
        Assert.Equal("refresh-1", tokens.RefreshToken); // Reddit usually doesn't rotate — keep the old one
        Assert.Contains("grant_type=refresh_token", handler.RequestBodies[0]);
        Assert.Equal("Basic", handler.Requests[0].Headers.Authorization!.Scheme);
    }

    [Fact]
    public async Task Refresh_WithoutRefreshToken_ReturnsNull_WithoutNetworkCall()
    {
        var handler = new SequenceHandler((HttpStatusCode.OK, "{}"));
        Assert.Null(await Make(handler).RefreshAsync(Ctx(refresh: null)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Metrics_MapsScoreAndComments_NoImpressions()
    {
        var handler = new SequenceHandler((HttpStatusCode.OK, """
            {"data":{"children":[{"data":{"score":42,"num_comments":7}}]}}
            """));
        var connector = Make(handler);

        var metrics = await connector.GetPostMetricsAsync(Ctx(), "t3_1abc");

        Assert.Equal(42, metrics!.Likes);
        Assert.Equal(7, metrics.Replies);
        Assert.Null(metrics.Reposts);       // crossposts aren't exposed as a simple count
        Assert.Null(metrics.Impressions);   // Reddit doesn't expose views to app users
        Assert.Contains("id=t3_1abc", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task Metrics_PostDeleted_ReturnsNull()
    {
        var handler = new SequenceHandler((HttpStatusCode.OK, """{"data":{"children":[]}}"""));
        Assert.Null(await Make(handler).GetPostMetricsAsync(Ctx(), "t3_gone"));
    }

    [Fact]
    public async Task ConnectAsync_AlwaysRejects_PointingAtOAuthFlow()
    {
        var connector = Make(new SequenceHandler());
        await Assert.ThrowsAsync<ArgumentException>(
            () => connector.ConnectAsync(new SocialCredentials("u", "p")));
    }
}
