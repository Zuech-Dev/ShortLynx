using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Admin.Components.Pages;
using ShortLynx.Data.Context;
using ShortLynx.Services.Links;

namespace ShortLynx.Tests.Admin;

public class LinksComponentTests : BunitContext
{
    private readonly FakeLinkService _links = new();
    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();
    private readonly Guid _accountId;
    // Overridable so a single test can render with custom codes denied (hosted-plan gate).
    private ShortLynx.Services.Entitlements.IEntitlements _entitlements = new ShortLynx.Services.Entitlements.UnlimitedEntitlements();

    public LinksComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ShortLynxDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>().CreateDbContext());
        Services.AddScoped<ILinkService>(_ => _links);
        // Custom-code UI dependencies: the create form resolves IEntitlements (gate) always, and
        // ICustomCodeService when a custom code is typed. Default to the OSS unlimited policy.
        Services.AddSingleton<ShortLynx.Services.Entitlements.IEntitlements>(_ => _entitlements);
        Services.AddSingleton(new ShortLynx.Services.ShortCodes.CustomCodeValidator(
            Microsoft.Extensions.Options.Options.Create(new ShortLynx.Services.ShortCodes.ShortCodeOptions())));
        Services.AddScoped<ShortLynx.Services.ShortCodes.ICustomCodeService, ShortLynx.Services.ShortCodes.CustomCodeService>();

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
    public void CreateLink_Valid_ShowsShortCode_AndCallsServiceWithUser()
    {
        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click();                       // + New link
        cut.Find("input.field-input").Change("https://example.com"); // URL
        cut.Find("form").Submit();

        Assert.Single(_links.Created);
        Assert.Equal(_accountId, _links.Created[0].AccountId);
        Assert.Contains(_links.CodeToReturn, cut.Markup);
        Assert.NotNull(cut.Find("[data-testid=new-link]"));
    }

    [Fact]
    public void CreateLink_ServiceRejectsUrl_ShowsError_NoLinkCreated()
    {
        _links.ThrowOnCreate = true;

        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click();
        cut.Find("input.field-input").Change("https://blocked.example.com");
        cut.Find("form").Submit();

        Assert.Empty(_links.Created);
        Assert.Contains("blocked URL", cut.Markup);
    }

    [Fact]
    public void CreateLink_WithCampaignSelected_PassesCampaignId()
    {
        Guid campaignId;
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            var c = new ShortLynx.Data.Entities.CampaignEntity
            {
                Id = Guid.CreateVersion7(), AccountId = _accountId, Name = "Launch", CreatedAt = DateTimeOffset.UtcNow,
            };
            db.CampaignEntities.Add(c);
            db.SaveChanges();
            campaignId = c.Id;
        }

        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click();
        cut.Find("input.field-input").Change("https://example.com");
        cut.Find("[data-testid=create-campaign]").Change(campaignId.ToString());
        cut.Find("form").Submit();

        Assert.Single(_links.Created);
        Assert.Equal(campaignId, _links.Created[0].CampaignId);
    }

    [Fact]
    public void CreateLink_UserAttributedMode_CallsMode2Path()
    {
        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click();                       // + New link
        cut.Find("input.field-input").Change("https://example.com"); // URL
        cut.Find("#mode-user").Change("UserAttributed");              // switch to user-attributed
        cut.Find("form").Submit();

        // Routed through the Mode-2 creation path, not the anonymous one.
        Assert.Empty(_links.Created);
        Assert.Single(_links.CreatedUserAttributed);
        Assert.Equal(_accountId, _links.CreatedUserAttributed[0].AccountId);
    }
    // ── Custom (vanity) codes ─────────────────────────────────────────────────

    private sealed class DenyCustomCodes : ShortLynx.Services.Entitlements.IEntitlements
    {
        public Task<bool> CanCreateLinkAsync(Guid a, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanCreateCustomCodeAsync(Guid a, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> IsFeatureEnabledAsync(Guid a, ShortLynx.Services.Entitlements.PlanFeature f, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanAddCustomDomainAsync(Guid a, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int?> GetRetentionDaysAsync(Guid a, CancellationToken ct = default) => Task.FromResult<int?>(null);
        public Task<bool> CanAddMemberAsync(Guid a, CancellationToken ct = default) => Task.FromResult(true);
    }

    [Fact]
    public void CustomCodeField_ShowsInAnonymousMode_WhenEntitled()
    {
        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click(); // + New link
        Assert.NotEmpty(cut.FindAll("[data-testid=custom-code]"));
        Assert.Empty(cut.FindAll("[data-testid=custom-code-upsell]"));
    }

    [Fact]
    public void CustomCodeField_HiddenInUserAttributedMode()
    {
        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click();
        cut.Find("#mode-user").Change("UserAttributed");
        Assert.Empty(cut.FindAll("[data-testid=custom-code]"));
    }

    [Fact]
    public void WhenNotEntitled_ShowsUpsell_NotField()
    {
        _entitlements = new DenyCustomCodes();
        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click();
        Assert.Empty(cut.FindAll("[data-testid=custom-code]"));
        Assert.NotEmpty(cut.FindAll("[data-testid=custom-code-upsell]"));
    }

    [Fact]
    public void TypingValidCode_ShowsAvailable()
    {
        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click();
        cut.Find("[data-testid=custom-code]").Input("my-code-12");
        // Debounced (~350ms) real availability check against the in-memory DB.
        cut.WaitForState(() => cut.Find("[data-testid=custom-code-status]").TextContent.Contains("Available"),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void TypingInvalidCode_ShowsReason()
    {
        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click();
        cut.Find("[data-testid=custom-code]").Input("no"); // too short
        cut.WaitForState(() => cut.Find("[data-testid=custom-code-status]").TextContent.Contains("at least"),
            TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void CreateWithCustomCode_PassesItToService()
    {
        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click();
        cut.Find("input.field-input").Change("https://example.com");
        cut.Find("[data-testid=custom-code]").Input("my-code-12");
        cut.Find("form").Submit();

        Assert.Single(_links.Created);
        Assert.Equal("my-code-12", _links.CreatedCustomCodes[0]);
    }
}
