using System.Net;
using System.Text;
using ShortLynx.Services.Social;

namespace ShortLynx.Tests.Services.Social;

public class BlueskyConnectorTests
{
    // Canned-response handler: no live network in CI, and we can inspect the request the connector sent.
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

    private static (BlueskyConnector Connector, StubHandler Handler) Make(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        return (new BlueskyConnector(new HttpClient(handler)), handler);
    }

    [Fact]
    public async Task Connect_ValidCredentials_ReturnsIdentityWithTokens()
    {
        var (connector, handler) = Make(HttpStatusCode.OK, """
            {"did":"did:plc:abc123","handle":"me.bsky.social","accessJwt":"access-token","refreshJwt":"refresh-token"}
            """);

        var identity = await connector.ConnectAsync(new SocialCredentials("me.bsky.social", "app-password"));

        Assert.Equal("did:plc:abc123", identity.ExternalAccountId);
        Assert.Equal("me.bsky.social", identity.Handle);
        Assert.Equal("access-token", identity.AccessToken);
        Assert.Equal("refresh-token", identity.RefreshToken);

        // Hit the default PDS's createSession endpoint with the supplied credentials.
        Assert.Equal("https://bsky.social/xrpc/com.atproto.server.createSession",
            handler.LastRequest!.RequestUri!.ToString());
        Assert.Contains("app-password", handler.LastRequestBody);
    }

    [Fact]
    public async Task Connect_CustomPds_UsesInstanceUrl()
    {
        var (connector, handler) = Make(HttpStatusCode.OK, """
            {"did":"did:plc:x","handle":"h","accessJwt":"a","refreshJwt":null}
            """);

        await connector.ConnectAsync(new SocialCredentials("h", "pw", "https://pds.example.com/"));

        Assert.StartsWith("https://pds.example.com/xrpc/", handler.LastRequest!.RequestUri!.ToString());
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest)]
    public async Task Connect_RejectedCredentials_ThrowsArgumentException(HttpStatusCode status)
    {
        var (connector, _) = Make(status, """{"error":"AuthenticationRequired"}""");

        await Assert.ThrowsAsync<ArgumentException>(
            () => connector.ConnectAsync(new SocialCredentials("me.bsky.social", "wrong")));
    }

    [Fact]
    public async Task Connect_ServerError_Throws_NotArgumentException()
    {
        var (connector, _) = Make(HttpStatusCode.InternalServerError, "{}");

        // A platform outage is not a credentials problem — it must not surface as a 400 to the caller.
        await Assert.ThrowsAsync<HttpRequestException>(
            () => connector.ConnectAsync(new SocialCredentials("me.bsky.social", "pw")));
    }
}
