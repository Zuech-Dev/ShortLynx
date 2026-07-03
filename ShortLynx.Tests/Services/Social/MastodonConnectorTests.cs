using System.Net;
using System.Text;
using ShortLynx.Services.Social;

namespace ShortLynx.Tests.Services.Social;

public class MastodonConnectorTests
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

    private static (MastodonConnector Connector, StubHandler Handler) Make(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return (new MastodonConnector(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Connect_ValidToken_ReturnsInstanceQualifiedIdentity()
    {
        var (connector, handler) = Make(HttpStatusCode.OK, """
            {"id":"12345","username":"anthony","acct":"anthony"}
            """);

        var identity = await connector.ConnectAsync(
            new SocialCredentials("anthony", "token-abc", "https://mastodon.social"));

        // External id is host-qualified: id 12345 on another instance must not collide in the upsert key.
        Assert.Equal("mastodon.social#12345", identity.ExternalAccountId);
        Assert.Equal("@anthony@mastodon.social", identity.Handle);
        Assert.Equal("token-abc", identity.AccessToken);
        Assert.Null(identity.RefreshToken);

        Assert.Equal("https://mastodon.social/api/v1/accounts/verify_credentials",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.LastRequest.Headers.Authorization!.Scheme);
        Assert.Equal("token-abc", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task Connect_BareHostInstanceUrl_GetsHttpsScheme()
    {
        var (connector, handler) = Make(HttpStatusCode.OK, """
            {"id":"1","username":"u","acct":"u"}
            """);

        await connector.ConnectAsync(new SocialCredentials("u", "t", "fosstodon.org"));

        Assert.StartsWith("https://fosstodon.org/", handler.LastRequest!.RequestUri!.ToString());
    }

    [Fact]
    public async Task Connect_MissingInstanceUrl_ThrowsWithoutNetworkCall()
    {
        var (connector, handler) = Make(HttpStatusCode.OK, "{}");

        await Assert.ThrowsAsync<ArgumentException>(
            () => connector.ConnectAsync(new SocialCredentials("u", "t")));
        Assert.Null(handler.LastRequest);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Connect_RejectedToken_ThrowsArgumentException(HttpStatusCode status)
    {
        var (connector, _) = Make(status, """{"error":"The access token is invalid"}""");

        await Assert.ThrowsAsync<ArgumentException>(
            () => connector.ConnectAsync(new SocialCredentials("u", "bad", "https://mastodon.social")));
    }
}
