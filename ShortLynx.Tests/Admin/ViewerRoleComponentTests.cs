using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShortLynx.Admin.Components.Pages;
using ShortLynx.Admin.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.ApiKeys;
using ShortLynx.Services.Campaigns;
using ShortLynx.Services.Domains;
using ShortLynx.Services.Links;
using ShortLynx.Services.Social;

namespace ShortLynx.Tests.Admin;

/// <summary>
/// A Viewer must get read-only dashboard pages: write controls not rendered (or disabled), and —
/// because Blazor Server executes handlers regardless of markup — the handlers themselves must
/// no-op. The Settings test clicks a disabled button on purpose to prove the handler guard, not
/// just the markup gate.
/// </summary>
public class ViewerRoleComponentTests : BunitContext
{
    private sealed class RecordingSocialService : ISocialConnectionService
    {
        public readonly List<SocialConnectionEntity> Connections = [];
        public int Writes;

        public Task<SocialConnectionEntity> ConnectAsync(
            Guid accountId, Guid? connectedByUserAccountId, SocialPlatform platform,
            SocialCredentials credentials, CancellationToken ct = default)
        { Writes++; throw new InvalidOperationException("Viewer must not reach the service."); }

        public Task<SocialConnectionEntity> ConnectFromIdentityAsync(
            Guid accountId, Guid? connectedByUserAccountId, SocialPlatform platform,
            SocialIdentity identity, string? instanceUrl = null, CancellationToken ct = default)
        { Writes++; throw new InvalidOperationException("Viewer must not reach the service."); }

        public Task<IReadOnlyList<SocialConnectionEntity>> ListAsync(Guid accountId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SocialConnectionEntity>>(Connections);

        public Task<bool> DisconnectAsync(Guid connectionId, Guid accountId, CancellationToken ct = default)
        { Writes++; return Task.FromResult(true); }
    }

    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();
    private readonly Guid _accountId;
    private readonly FakeApiKeyService _keys = new();
    private readonly FakeCustomDomainService _domains = new();
    private readonly FakeLinkService _links = new();
    private readonly RecordingSocialService _social = new();

    public ViewerRoleComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ShortLynxDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>().CreateDbContext());
        Services.AddScoped<IApiKeyService>(_ => _keys);
        Services.AddScoped<ICustomDomainService>(_ => _domains);
        Services.AddScoped<ILinkService>(_ => _links);
        Services.AddScoped<ISocialConnectionService>(_ => _social);
        Services.AddScoped<ICampaignService, CampaignService>();
        Services.AddSingleton<IOptions<CustomDomainOptions>>(Options.Create(new CustomDomainOptions()));
        Services.AddSingleton<IOptions<DashboardOptions>>(Options.Create(new DashboardOptions()));

        var auth = AddAuthorization();
        auth.SetAuthorized("viewer@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, _uid.ToString()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        _accountId = AccountTestSeed.Seed(db, _uid, AccountRole.Viewer, "viewer@example.com");
    }

    [Fact]
    public void Links_Viewer_HasNoCreateButton()
    {
        var cut = Render<Links>();
        Assert.Empty(cut.FindAll("button.btn-primary"));
    }

    [Fact]
    public void Domains_Viewer_HasNoWriteButtons()
    {
        _domains.Domains.Add(new CustomDomainEntity
        {
            Id = Guid.CreateVersion7(), AccountId = _accountId, Domain = "go.example.com",
            CreatedAt = DateTimeOffset.UtcNow, IsActive = false,
            VerificationStatus = DomainVerificationStatus.Pending, VerificationToken = "tok",
        });

        var cut = Render<Domains>();
        Assert.Contains("go.example.com", cut.Markup);          // row is visible (reads allowed)
        Assert.Empty(cut.FindAll("button.btn-primary"));         // no "+ Add domain"
        Assert.Empty(cut.FindAll("[data-testid=verify-btn]"));
        Assert.Empty(cut.FindAll("[data-testid=remove-btn]"));
    }

    [Fact]
    public void ApiKeys_Viewer_SeesKeysButNoWriteButtons()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            db.Add(new ApiKeyEntity
            {
                Id = Guid.CreateVersion7(), Name = "CI key", Prefix = "ABCDEF12", KeyHash = "h",
                Scopes = Scopes.LinksRead, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
                AccountId = _accountId,
            });
            db.SaveChanges();
        }

        var cut = Render<ApiKeys>();
        Assert.Contains("CI key", cut.Markup);                   // reads allowed
        Assert.Empty(cut.FindAll("button.btn-primary"));         // no "+ New key"
        Assert.Empty(cut.FindAll("button.btn-outline-danger"));  // no Revoke
    }

    [Fact]
    public void Campaigns_Viewer_SeesRowsButNoWriteButtons()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            db.Add(new CampaignEntity
            {
                Id = Guid.CreateVersion7(), AccountId = _accountId, Name = "Launch",
                CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        var cut = Render<Campaigns>();
        Assert.Contains("Launch", cut.Markup);
        Assert.NotEmpty(cut.FindAll("[data-testid=view-analytics]")); // analytics stays (read)
        Assert.Empty(cut.FindAll("[data-testid=new-campaign]"));
        Assert.Empty(cut.FindAll("[data-testid=edit-btn]"));
        Assert.Empty(cut.FindAll("[data-testid=remove-btn]"));
    }

    [Fact]
    public void Social_Viewer_SeesConnectionsButNoWriteButtons()
    {
        _social.Connections.Add(new SocialConnectionEntity
        {
            Id = Guid.CreateVersion7(), AccountId = _accountId, Platform = SocialPlatform.Bluesky,
            ExternalAccountId = "ext", Handle = "someone.bsky.social",
            AccessTokenProtected = "enc:x", CreatedAt = DateTimeOffset.UtcNow,
        });

        var cut = Render<Social>();
        Assert.Contains("someone.bsky.social", cut.Markup);
        Assert.Empty(cut.FindAll("[data-testid=connect-toggle]"));
        Assert.Empty(cut.FindAll("[data-testid=connect-threads]"));
        Assert.Empty(cut.FindAll("[data-testid=disconnect-btn]"));
    }

    [Fact]
    public void Settings_Viewer_SaveDisabled_AndHandlerNoOps()
    {
        var cut = Render<Settings>();

        var save = cut.Find("[data-testid=privacy-save]");
        Assert.True(save.HasAttribute("disabled"));

        // bUnit dispatches clicks even on disabled elements — this exercises the handler guard,
        // proving enforcement doesn't rely on the disabled attribute alone. (URL binds on oninput.)
        cut.Find("[data-testid=privacy-url]").Input("https://evil.example.com/policy");
        cut.Find("[data-testid=privacy-confirm]").Change(true);
        save.Click();

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        var account = db.AccountEntities.Single(a => a.Id == _accountId);
        Assert.Null(account.PrivacyPolicyUrl); // unchanged — the guard held
    }

    protected override void Dispose(bool disposing)
    {
        _conn.Dispose();
        base.Dispose(disposing);
    }
}
