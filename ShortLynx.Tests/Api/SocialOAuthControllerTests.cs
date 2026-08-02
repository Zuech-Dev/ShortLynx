using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Social;

namespace ShortLynx.Tests.Api;

// SocialOAuthController replaces ShortLynx.Admin's former MapSocialOAuth (Program.cs) so Threads/Reddit
// OAuth no longer depends on Admin's cookie session. Full OAuth round-trips against a live Meta/Reddit
// sandbox aren't practical in CI — the connector's own exchange logic is covered at the unit level
// (ThreadsConnectorTests) — so this drives the controller end-to-end against a faked
// IOAuthSocialConnector the same way MeSocialTests fakes ISocialConnector for Bluesky/Mastodon.
public class SocialOAuthControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public SocialOAuthControllerTests(ApiFactory factory) => _factory = factory;

    private sealed class FakeOAuthConnector(SocialPlatform platform) : IOAuthSocialConnector
    {
        public SocialPlatform Platform => platform;
        public bool RejectCode;
        public string? LastRedirectUriReceived;

        public string BuildAuthorizeUrl(string redirectUri, string state)
            => $"https://fake-platform.example/authorize?redirect_uri={Uri.EscapeDataString(redirectUri)}&state={state}";

        public Task<SocialIdentity> ExchangeAuthorizationCodeAsync(string code, string redirectUri, CancellationToken ct = default)
        {
            LastRedirectUriReceived = redirectUri;
            if (RejectCode) throw new ArgumentException($"{platform} rejected the authorization code.");
            return Task.FromResult(new SocialIdentity($"ext-{platform}", $"@me-{platform}", "access-token", "refresh-token", null));
        }

        public Task<SocialIdentity> ConnectAsync(SocialCredentials credentials, CancellationToken ct = default)
            => throw new NotSupportedException("OAuth-only platform.");

        public Task<SocialPostRef> PublishAsync(SocialConnectionContext connection, string text, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SocialTokens?> RefreshAsync(SocialConnectionContext connection, CancellationToken ct = default)
            => Task.FromResult<SocialTokens?>(null);

        public Task<SocialPostMetrics?> GetPostMetricsAsync(SocialConnectionContext connection, string externalPostId, CancellationToken ct = default)
            => Task.FromResult<SocialPostMetrics?>(null);
    }

    // Host with the real pipeline, Threads/Reddit "configured" (test AppId/AppSecret + a known
    // ReturnUrlBase so redirects are assertable), and the fake connector swapped in for both OAuth slots.
    private (WebApplicationFactory<ShortLynx.Core.CoreApiEntryPoint> Host, FakeOAuthConnector Threads, FakeOAuthConnector Reddit) ConfiguredHost()
    {
        var threadsFake = new FakeOAuthConnector(SocialPlatform.Threads);
        var redditFake = new FakeOAuthConnector(SocialPlatform.Reddit);

        var host = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Threads:AppId"] = "test-threads-app-id",
                ["Threads:AppSecret"] = "test-threads-app-secret",
                ["Reddit:AppId"] = "test-reddit-app-id",
                ["Reddit:AppSecret"] = "test-reddit-app-secret",
                ["SocialOAuth:ReturnUrlBase"] = "https://app.test.example/social",
            }));
            b.ConfigureServices(s =>
            {
                s.RemoveAll<ISocialConnector>();
                s.AddSingleton<ISocialConnector>(threadsFake);
                s.AddSingleton<ISocialConnector>(redditFake);
            });
        });

        return (host, threadsFake, redditFake);
    }

    private static HttpClient NoRedirectClient(WebApplicationFactory<ShortLynx.Core.CoreApiEntryPoint> host)
        => host.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
        });

    private async Task<HttpClient> AuthenticatedClientAsync(
        WebApplicationFactory<ShortLynx.Core.CoreApiEntryPoint> host, AccountRole role = AccountRole.Owner)
    {
        // ApiFactory's session helpers are on the base factory; the token they mint is valid against any
        // WithWebHostBuilder derivative sharing the same in-memory database/signing key.
        var (token, _, _) = await _factory.SeedMemberTokenAsync(role);
        var session = await (await host.CreateClient().PostAsJsonAsync(
                "/auth/session", new ShortLynx.Core.Models.Requests.CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<ShortLynx.Core.Models.Responses.SessionResponse>();

        var client = NoRedirectClient(host);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {session!.AccessToken}");
        return client;
    }

    // Uri.Query throws for a relative URI (the "not_configured"/error redirects are relative when
    // SocialOAuth:ReturnUrlBase is left at its default "/social"), so the query is split out by hand
    // rather than relying on Uri's absolute-only accessor.
    private static string? QueryValue(Uri uri, string key)
    {
        var raw = uri.IsAbsoluteUri ? uri.PathAndQuery : uri.OriginalString;
        var queryStart = raw.IndexOf('?');
        var query = queryStart >= 0 ? raw[queryStart..] : string.Empty;
        return QueryHelpers.ParseQuery(query).TryGetValue(key, out var v) ? v.ToString() : null;
    }

    // ── Authorize ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Authorize_WithoutSession_Returns401()
    {
        var resp = await _factory.CreateClient().GetAsync("/social/threads/authorize");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Authorize_UnknownPlatform_Returns404()
    {
        var (host, _, _) = ConfiguredHost();
        var client = await AuthenticatedClientAsync(host);

        var resp = await client.GetAsync("/social/bluesky/authorize");

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Authorize_NotConfigured_RedirectsWithError()
    {
        // Default ApiFactory config has no Threads:AppId/AppSecret at all.
        var (token, _, _) = await _factory.SeedMemberTokenAsync();
        var session = await (await _factory.CreateClient().PostAsJsonAsync(
                "/auth/session", new ShortLynx.Core.Models.Requests.CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<ShortLynx.Core.Models.Responses.SessionResponse>();
        var client = NoRedirectClient(_factory);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {session!.AccessToken}");

        var resp = await client.GetAsync("/social/threads/authorize");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("not_configured", QueryValue(resp.Headers.Location!, "threadsError"));
    }

    [Fact]
    public async Task Authorize_Configured_SetsStateCookie_AndRedirectsToConnectorUrl()
    {
        var (host, _, _) = ConfiguredHost();
        var client = await AuthenticatedClientAsync(host);

        var resp = await client.GetAsync("/social/threads/authorize");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Contains("fake-platform.example/authorize", resp.Headers.Location!.OriginalString);
        Assert.True(resp.Headers.TryGetValues("Set-Cookie", out var cookies));
        Assert.Contains(cookies!, c => c.StartsWith("sl_threads_oauth_state="));
    }

    // ── Callback ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Callback_WithoutSession_Returns401()
    {
        var resp = await _factory.CreateClient().GetAsync("/social/threads/callback?code=x&state=y");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Callback_PlatformReportedError_RedirectsWithThatError()
    {
        var (host, _, _) = ConfiguredHost();
        var client = await AuthenticatedClientAsync(host);

        var resp = await client.GetAsync("/social/threads/callback?error=access_denied");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("access_denied", QueryValue(resp.Headers.Location!, "threadsError"));
    }

    [Fact]
    public async Task Callback_NoStateCookiePresent_RedirectsWithMissingState()
    {
        var (host, _, _) = ConfiguredHost();
        var client = await AuthenticatedClientAsync(host);

        var resp = await client.GetAsync("/social/threads/callback?code=abc&state=whatever");

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("missing_state", QueryValue(resp.Headers.Location!, "threadsError"));
    }

    [Fact]
    public async Task Callback_StateDoesNotMatchCookie_RedirectsWithStateMismatch()
    {
        var (host, _, _) = ConfiguredHost();
        var client = await AuthenticatedClientAsync(host);

        var authorizeResp = await client.GetAsync("/social/threads/authorize");
        var stateCookie = authorizeResp.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("sl_threads_oauth_state="));

        var request = new HttpRequestMessage(HttpMethod.Get, "/social/threads/callback?code=abc&state=not-the-real-state");
        request.Headers.Add("Cookie", stateCookie.Split(';')[0]);
        var resp = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("state_mismatch", QueryValue(resp.Headers.Location!, "threadsError"));
    }

    [Fact]
    public async Task Callback_MissingCode_RedirectsWithMissingCode()
    {
        var (host, _, _) = ConfiguredHost();
        var client = await AuthenticatedClientAsync(host);

        var authorizeResp = await client.GetAsync("/social/threads/authorize");
        var (stateCookie, state) = ExtractCookieAndState(authorizeResp, "threads");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/social/threads/callback?state={state}");
        request.Headers.Add("Cookie", stateCookie);
        var resp = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("missing_code", QueryValue(resp.Headers.Location!, "threadsError"));
    }

    [Fact]
    public async Task Callback_ViewerRole_RedirectsWithForbidden_AndCreatesNoConnection()
    {
        var (host, _, _) = ConfiguredHost();
        var client = await AuthenticatedClientAsync(host, AccountRole.Viewer);

        var authorizeResp = await client.GetAsync("/social/threads/authorize");
        var (stateCookie, state) = ExtractCookieAndState(authorizeResp, "threads");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/social/threads/callback?code=abc&state={state}");
        request.Headers.Add("Cookie", stateCookie);
        var resp = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("forbidden", QueryValue(resp.Headers.Location!, "threadsError"));
    }

    [Fact]
    public async Task Callback_ConnectorRejectsCode_RedirectsWithConnectorMessage()
    {
        var (host, threadsFake, _) = ConfiguredHost();
        threadsFake.RejectCode = true;
        var client = await AuthenticatedClientAsync(host);

        var authorizeResp = await client.GetAsync("/social/threads/authorize");
        var (stateCookie, state) = ExtractCookieAndState(authorizeResp, "threads");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/social/threads/callback?code=bad-code&state={state}");
        request.Headers.Add("Cookie", stateCookie);
        var resp = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("Threads rejected the authorization code.", QueryValue(resp.Headers.Location!, "threadsError"));
    }

    [Fact]
    public async Task Callback_Success_CreatesConnection_UsesRegisteredRedirectUri_AndRedirectsWithConnected()
    {
        var (host, threadsFake, _) = ConfiguredHost();
        var (token, _, accountId) = await _factory.SeedMemberTokenAsync();
        var session = await (await host.CreateClient().PostAsJsonAsync(
                "/auth/session", new ShortLynx.Core.Models.Requests.CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<ShortLynx.Core.Models.Responses.SessionResponse>();
        var client = NoRedirectClient(host);
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {session!.AccessToken}");

        var authorizeResp = await client.GetAsync("/social/threads/authorize");
        var (stateCookie, state) = ExtractCookieAndState(authorizeResp, "threads");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/social/threads/callback?code=good-code&state={state}");
        request.Headers.Add("Cookie", stateCookie);
        var resp = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("https://app.test.example/social?connected=threads", resp.Headers.Location!.OriginalString);

        // The exchange must be signed against the platform's registered RedirectUri, not an ad hoc value.
        Assert.False(string.IsNullOrEmpty(threadsFake.LastRedirectUriReceived));

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
        var connection = await db.SocialConnectionEntities.SingleAsync(c => c.AccountId == accountId);
        Assert.Equal(SocialPlatform.Threads, connection.Platform);
        Assert.Equal("ext-Threads", connection.ExternalAccountId);

        // "Single use" is enforced by instructing the browser to forget the cookie, not by any
        // server-side used-token ledger — a byte-for-byte replay of the same Cookie header is
        // indistinguishable from a fresh one server-side, so what's actually verifiable here is that the
        // response tells the browser to delete it (an expired/zero Max-Age Set-Cookie for the same name).
        var clearingCookie = resp.Headers.GetValues("Set-Cookie").Single(c => c.StartsWith("sl_threads_oauth_state="));
        Assert.Matches("expires=|max-age=0", clearingCookie.ToLowerInvariant());
    }

    [Fact]
    public async Task Callback_Reddit_UsesRedditFake_Independently()
    {
        var (host, _, redditFake) = ConfiguredHost();
        var client = await AuthenticatedClientAsync(host);

        var authorizeResp = await client.GetAsync("/social/reddit/authorize");
        var (stateCookie, state) = ExtractCookieAndState(authorizeResp, "reddit");

        var request = new HttpRequestMessage(HttpMethod.Get, $"/social/reddit/callback?code=good-code&state={state}");
        request.Headers.Add("Cookie", stateCookie);
        var resp = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Redirect, resp.StatusCode);
        Assert.Equal("https://app.test.example/social?connected=reddit", resp.Headers.Location!.OriginalString);
        Assert.False(string.IsNullOrEmpty(redditFake.LastRedirectUriReceived));
    }

    private static (string CookieHeader, string State) ExtractCookieAndState(HttpResponseMessage authorizeResp, string slug)
    {
        var stateCookie = authorizeResp.Headers.GetValues("Set-Cookie").First(c => c.StartsWith($"sl_{slug}_oauth_state="));
        var state = QueryValue(authorizeResp.Headers.Location!, "state")
                    ?? throw new InvalidOperationException("Fake connector's authorize URL carried no state.");
        return (stateCookie.Split(';')[0], state);
    }
}
