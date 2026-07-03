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

public class MeSocialTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeSocialTests(ApiFactory factory) => _factory = factory;

    private sealed class FakeBlueskyConnector : ISocialConnector
    {
        public SocialPlatform Platform => SocialPlatform.Bluesky;
        public bool RejectCredentials;

        public Task<SocialIdentity> ConnectAsync(SocialCredentials credentials, CancellationToken ct = default)
            => RejectCredentials
                ? throw new ArgumentException("Bluesky rejected the handle or app password.")
                : Task.FromResult(new SocialIdentity("did:plc:test", credentials.Identifier, "access-jwt", "refresh-jwt", null));

        public Task<SocialPostRef> PublishAsync(SocialConnectionContext connection, string text, CancellationToken ct = default)
            => Task.FromResult(new SocialPostRef("at://did:plc:test/app.bsky.feed.post/abc", "https://bsky.app/profile/x/post/abc"));

        public Task<SocialTokens?> RefreshAsync(SocialConnectionContext connection, CancellationToken ct = default)
            => Task.FromResult<SocialTokens?>(null);
    }

    // Host with the real pipeline but a faked platform connector (no live network in CI).
    private async Task<HttpClient> CreateClientAsync(FakeBlueskyConnector fake)
    {
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
        return client;
    }

    [Fact]
    public async Task Connect_ThenList_ShowsConnection_WithoutTokens()
    {
        var client = await CreateClientAsync(new FakeBlueskyConnector());

        var resp = await client.PostAsJsonAsync("/me/social",
            new ConnectSocialRequest("Bluesky", "me.bsky.social", "app-password"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);

        var created = await resp.Content.ReadFromJsonAsync<SocialConnectionResponse>();
        Assert.Equal("Bluesky", created!.Platform);
        Assert.Equal("me.bsky.social", created.Handle);

        var listResp = await client.GetAsync("/me/social");
        var raw = await listResp.Content.ReadAsStringAsync();
        var list = await listResp.Content.ReadFromJsonAsync<List<SocialConnectionResponse>>();
        Assert.Single(list!);

        // The API payload must never leak tokens — plaintext or ciphertext.
        Assert.DoesNotContain("access-jwt", raw);
        Assert.DoesNotContain("refresh-jwt", raw);
        Assert.DoesNotContain("Protected", raw);
    }

    [Fact]
    public async Task Connect_BadCredentials_Returns400()
    {
        var client = await CreateClientAsync(new FakeBlueskyConnector { RejectCredentials = true });

        var resp = await client.PostAsJsonAsync("/me/social",
            new ConnectSocialRequest("Bluesky", "me.bsky.social", "wrong-password"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Connect_UnknownPlatform_Returns400()
    {
        var client = await CreateClientAsync(new FakeBlueskyConnector());

        var resp = await client.PostAsJsonAsync("/me/social",
            new ConnectSocialRequest("MySpace", "tom", "pw"));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Disconnect_RemovesConnection_And_ForeignId_Returns404()
    {
        var client = await CreateClientAsync(new FakeBlueskyConnector());
        var created = await (await client.PostAsJsonAsync("/me/social",
                new ConnectSocialRequest("Bluesky", "me.bsky.social", "pw")))
            .Content.ReadFromJsonAsync<SocialConnectionResponse>();

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/me/social/{Guid.CreateVersion7()}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/me/social/{created!.Id}")).StatusCode);

        var list = await (await client.GetAsync("/me/social")).Content.ReadFromJsonAsync<List<SocialConnectionResponse>>();
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Social_WithoutSession_Returns401()
    {
        var resp = await _factory.CreateClient().GetAsync("/me/social");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
