using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Admin.Components.Pages;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Social;

namespace ShortLynx.Tests.Admin;

public class SocialComponentTests : BunitContext
{
    private sealed class FakeSocialConnectionService : ISocialConnectionService
    {
        public readonly List<SocialConnectionEntity> Connections = [];
        public readonly List<(Guid AccountId, SocialPlatform Platform, SocialCredentials Credentials)> ConnectCalls = [];
        public readonly List<(Guid ConnectionId, Guid AccountId)> DisconnectCalls = [];
        public bool RejectCredentials;

        public Task<SocialConnectionEntity> ConnectAsync(
            Guid accountId, Guid? connectedByUserAccountId, SocialPlatform platform,
            SocialCredentials credentials, CancellationToken ct = default)
        {
            if (RejectCredentials) throw new ArgumentException("The platform rejected the credentials.");
            ConnectCalls.Add((accountId, platform, credentials));
            var entity = new SocialConnectionEntity
            {
                Id = Guid.CreateVersion7(), AccountId = accountId, Platform = platform,
                ExternalAccountId = "ext", Handle = credentials.Identifier, InstanceUrl = credentials.InstanceUrl,
                AccessTokenProtected = "enc:x", CreatedAt = DateTimeOffset.UtcNow,
            };
            Connections.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<SocialConnectionEntity> ConnectFromIdentityAsync(
            Guid accountId, Guid? connectedByUserAccountId, SocialPlatform platform,
            SocialIdentity identity, string? instanceUrl = null, CancellationToken ct = default)
        {
            var entity = new SocialConnectionEntity
            {
                Id = Guid.CreateVersion7(), AccountId = accountId, Platform = platform,
                ExternalAccountId = identity.ExternalAccountId, Handle = identity.Handle, InstanceUrl = instanceUrl,
                AccessTokenProtected = "enc:x", CreatedAt = DateTimeOffset.UtcNow,
            };
            Connections.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<IReadOnlyList<SocialConnectionEntity>> ListAsync(Guid accountId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SocialConnectionEntity>>(
                Connections.Where(c => c.AccountId == accountId).ToList());

        public Task<bool> DisconnectAsync(Guid connectionId, Guid accountId, CancellationToken ct = default)
        {
            DisconnectCalls.Add((connectionId, accountId));
            Connections.RemoveAll(c => c.Id == connectionId);
            return Task.FromResult(true);
        }
    }

    private readonly FakeSocialConnectionService _social = new();
    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();
    private readonly Guid _accountId;

    public SocialComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ISocialConnectionService>(_ => _social);

        var auth = AddAuthorization();
        auth.SetAuthorized("user@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, _uid.ToString()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        _accountId = AccountTestSeed.SeedOwner(db, _uid);
    }

    [Fact]
    public void ConnectThreadsLink_PointsAtOAuthAuthorizeEndpoint()
    {
        var cut = Render<Social>();

        var link = cut.Find("[data-testid=connect-threads]");
        Assert.Equal("/social/threads/authorize", link.GetAttribute("href"));
    }

    [Fact]
    public void ConnectRedditLink_PointsAtOAuthAuthorizeEndpoint()
    {
        var cut = Render<Social>();

        var link = cut.Find("[data-testid=connect-reddit]");
        Assert.Equal("/social/reddit/authorize", link.GetAttribute("href"));
    }

    [Fact]
    public void RedditErrorQueryParam_ShowsFriendlyErrorBanner()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/social?redditError=not_configured");

        var cut = Render<Social>();

        var banner = cut.Find("[data-testid=threads-error]").TextContent;
        Assert.Contains("Reddit", banner);
        Assert.Contains("Reddit:AppId", banner);
    }

    [Fact]
    public void ConnectedReddit_ShowsSuccessBanner()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/social?connected=reddit");

        var cut = Render<Social>();

        Assert.Contains("Reddit account connected", cut.Find("[data-testid=threads-connected]").TextContent);
    }

    [Fact]
    public void ConnectedQueryParam_ShowsThreadsSuccessBanner()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/social?connected=threads");

        var cut = Render<Social>();

        cut.WaitForElement("[data-testid=threads-connected]");
    }

    [Fact]
    public void ThreadsErrorQueryParam_ShowsFriendlyErrorBanner()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/social?threadsError=state_mismatch");

        var cut = Render<Social>();

        // Short error codes from the OAuth endpoints map to human-readable text, not raw codes.
        Assert.Contains("state check failed", cut.Find("[data-testid=threads-error]").TextContent);
    }

    [Fact]
    public void ThreadsNotConfigured_ShowsOperatorGuidance()
    {
        Services.GetRequiredService<NavigationManager>().NavigateTo("/social?threadsError=not_configured");

        var cut = Render<Social>();

        Assert.Contains("Threads:AppId", cut.Find("[data-testid=threads-error]").TextContent);
    }

    [Fact]
    public void EmptyState_Renders()
    {
        var cut = Render<Social>();
        cut.WaitForElement("[data-testid=social-empty]");
    }

    [Fact]
    public void Connect_Bluesky_CallsService_AndListsConnection()
    {
        var cut = Render<Social>();

        cut.Find("[data-testid=connect-toggle]").Click();
        cut.Find("[data-testid=connect-identifier]").Change("me.bsky.social");
        cut.Find("[data-testid=connect-secret]").Change("app-password");
        cut.Find("form").Submit();

        var call = Assert.Single(_social.ConnectCalls);
        Assert.Equal(_accountId, call.AccountId);
        Assert.Equal(SocialPlatform.Bluesky, call.Platform);
        Assert.Equal("me.bsky.social", call.Credentials.Identifier);
        Assert.Equal("app-password", call.Credentials.Secret);

        var row = cut.WaitForElement("[data-testid=social-row]");
        Assert.Contains("me.bsky.social", row.InnerHtml);
    }

    [Fact]
    public void Connect_Mastodon_ShowsInstanceField_AndPassesInstanceUrl()
    {
        var cut = Render<Social>();

        cut.Find("[data-testid=connect-toggle]").Click();
        cut.Find("[data-testid=connect-platform]").Change("Mastodon");
        cut.Find("[data-testid=connect-instance]").Change("https://fosstodon.org");
        cut.Find("[data-testid=connect-secret]").Change("token-abc");
        cut.Find("[data-testid=connect-identifier]").Change("@me@fosstodon.org");
        cut.Find("form").Submit();

        var call = Assert.Single(_social.ConnectCalls);
        Assert.Equal(SocialPlatform.Mastodon, call.Platform);
        Assert.Equal("https://fosstodon.org", call.Credentials.InstanceUrl);
    }

    [Fact]
    public void Connect_Rejected_ShowsError()
    {
        _social.RejectCredentials = true;
        var cut = Render<Social>();

        cut.Find("[data-testid=connect-toggle]").Click();
        cut.Find("[data-testid=connect-identifier]").Change("me.bsky.social");
        cut.Find("[data-testid=connect-secret]").Change("bad");
        cut.Find("form").Submit();

        Assert.Contains("rejected", cut.Find("[data-testid=connect-error]").TextContent);
        Assert.Empty(_social.ConnectCalls);
    }

    [Fact]
    public void Disconnect_CallsService_WithAccountScope()
    {
        _social.Connections.Add(new SocialConnectionEntity
        {
            Id = Guid.CreateVersion7(), AccountId = _accountId, Platform = SocialPlatform.Bluesky,
            ExternalAccountId = "did:plc:x", Handle = "me.bsky.social",
            AccessTokenProtected = "enc:x", CreatedAt = DateTimeOffset.UtcNow,
        });

        var cut = Render<Social>();
        cut.WaitForElement("[data-testid=disconnect-btn]").Click();

        var call = Assert.Single(_social.DisconnectCalls);
        Assert.Equal(_accountId, call.AccountId);
        cut.WaitForElement("[data-testid=social-empty]");
    }
}
