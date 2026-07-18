using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Social;

namespace ShortLynx.Tests.Api;

public class MeLinkPublishTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeLinkPublishTests(ApiFactory factory) => _factory = factory;

    private sealed class FakeBlueskyConnector : ISocialConnector
    {
        public string? LastPublishedText;
        public SocialPlatform Platform => SocialPlatform.Bluesky;

        public Task<SocialIdentity> ConnectAsync(SocialCredentials credentials, CancellationToken ct = default)
            => Task.FromResult(new SocialIdentity("did:plc:test", credentials.Identifier, "access-jwt", "refresh-jwt", null));

        public Task<SocialPostRef> PublishAsync(SocialConnectionContext connection, string text, CancellationToken ct = default)
        {
            LastPublishedText = text;
            return Task.FromResult(new SocialPostRef("at://did:plc:test/app.bsky.feed.post/rk1",
                "https://bsky.app/profile/me.bsky.social/post/rk1"));
        }

        public Task<SocialTokens?> RefreshAsync(SocialConnectionContext connection, CancellationToken ct = default)
            => Task.FromResult<SocialTokens?>(null);

        public SocialPostMetrics? Metrics;

        public Task<SocialPostMetrics?> GetPostMetricsAsync(SocialConnectionContext connection, string externalPostId, CancellationToken ct = default)
            => Task.FromResult(Metrics);
    }

    private async Task<(HttpClient Client, FakeBlueskyConnector Connector)> CreateClientAsync()
    {
        var fake = new FakeBlueskyConnector();
        var (token, _, _) = await _factory.SeedMemberTokenAsync();
        var host = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<ISocialConnector>();
            s.AddSingleton<ISocialConnector>(fake);
        }));

        var session = await (await host.CreateClient().PostAsJsonAsync(
                "/auth/session", new CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<SessionResponse>();

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {session!.AccessToken}");
        return (client, fake);
    }

    private static async Task<Guid> ConnectAsync(HttpClient client)
        => (await (await client.PostAsJsonAsync("/me/social",
                new ConnectSocialRequest("Bluesky", "me.bsky.social", "app-password")))
            .Content.ReadFromJsonAsync<SocialConnectionResponse>())!.Id;

    [Fact]
    public async Task Publish_AnonymousLink_PostsShortUrl_AndRecordsHistory()
    {
        var (client, connector) = await CreateClientAsync();
        var connectionId = await ConnectAsync(client);
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/launch")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var resp = await client.PostAsJsonAsync($"/me/links/{link!.Id}/publish",
            new PublishLinkRequest([connectionId], "Big news!"));
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var results = await resp.Content.ReadFromJsonAsync<List<PublishTargetResponse>>();
        var target = Assert.Single(results!);
        Assert.True(target.Success);
        Assert.Equal("https://bsky.app/profile/me.bsky.social/post/rk1", target.PostUrl);

        // The composed post carries a per-post code (NOT the link's shared code) so its clicks
        // attribute exactly to this post — that's the whole point of per-post attribution.
        Assert.StartsWith("Big news!", connector.LastPublishedText);
        Assert.DoesNotContain(link.ShortCode, connector.LastPublishedText);

        var posts = await (await client.GetAsync($"/me/links/{link.Id}/posts"))
            .Content.ReadFromJsonAsync<List<SocialPostResponse>>();
        var post = Assert.Single(posts!);
        Assert.Equal("Bluesky", post.Platform);
        Assert.Null(post.Impressions);
    }

    [Fact]
    public async Task RefreshPosts_PullsMetrics_AndReturnsUpdatedPosts()
    {
        var (client, connector) = await CreateClientAsync();
        var connectionId = await ConnectAsync(client);
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();
        await client.PostAsJsonAsync($"/me/links/{link!.Id}/publish", new PublishLinkRequest([connectionId], "hi"));

        connector.Metrics = new SocialPostMetrics(Impressions: null, Likes: 12, Reposts: 4, Replies: 2);
        var resp = await client.PostAsync($"/me/links/{link.Id}/posts/refresh", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var posts = await resp.Content.ReadFromJsonAsync<List<SocialPostResponse>>();
        var post = Assert.Single(posts!);
        Assert.Equal(12, post.Likes);
        Assert.Equal(4, post.Reposts);
        Assert.Equal(2, post.Replies);
        Assert.Null(post.Impressions);          // Tier-A platforms don't report views
        Assert.NotNull(post.MetricsUpdatedAt);
    }

    [Fact]
    public async Task Publish_UserAttributedLink_Returns400()
    {
        var (client, _) = await CreateClientAsync();
        var connectionId = await ConnectAsync(client);
        var link = await (await client.PostAsJsonAsync("/me/links",
                new CreateMyLinkRequest("https://example.com", "UserAttributed")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var resp = await client.PostAsJsonAsync($"/me/links/{link!.Id}/publish",
            new PublishLinkRequest([connectionId], "hi"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Publish_ForeignLink_Returns404()
    {
        var (clientA, _) = await CreateClientAsync();
        var (clientB, _) = await CreateClientAsync();
        var connectionB = await ConnectAsync(clientB);
        var linkA = await (await clientA.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var resp = await clientB.PostAsJsonAsync($"/me/links/{linkA!.Id}/publish",
            new PublishLinkRequest([connectionB], "hi"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
