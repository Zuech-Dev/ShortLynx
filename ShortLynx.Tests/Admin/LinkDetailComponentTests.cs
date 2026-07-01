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
using ShortLynx.Services.Links;

namespace ShortLynx.Tests.Admin;

public class LinkDetailComponentTests : BunitContext
{
    private readonly FakeLinkService _links = new();
    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();
    private readonly Guid _accountId;

    public LinkDetailComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ILinkService>(_ => _links);
        Services.AddSingleton<IOptions<DashboardOptions>>(
            Options.Create(new DashboardOptions { PublicBaseUrl = "https://s.example" }));

        var auth = AddAuthorization();
        auth.SetAuthorized("user@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, _uid.ToString()));

        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        _accountId = AccountTestSeed.SeedOwner(db, _uid);
    }

    private Guid SeedUserAttributedLink()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        var link = new LinkEntity
        {
            Id = Guid.CreateVersion7(),
            OriginalUrl = "https://example.com",
            Mode = LinkMode.UserAttributed,
            AccountId = _accountId,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };
        db.LinkEntities.Add(link);
        db.SaveChanges();
        return link.Id;
    }

    [Fact]
    public void ProvisionPanel_RendersForUserAttributedLink()
    {
        var id = SeedUserAttributedLink();
        var cut = Render<LinkDetail>(p => p.Add(c => c.Id, id));

        Assert.NotNull(cut.Find("[data-testid=provision-input]"));
        Assert.NotNull(cut.Find("[data-testid=provision-submit]"));
    }

    [Fact]
    public void Provision_SubmitsLabels_WithOneTimeFlag_AndShowsMintedUrls()
    {
        var id = SeedUserAttributedLink();
        var cut = Render<LinkDetail>(p => p.Add(c => c.Id, id));

        cut.Find("[data-testid=provision-input]").Change("alice@example.com\nbob@example.com");
        cut.Find("#one-time").Change(true);
        cut.Find("[data-testid=provision-submit]").Click();

        Assert.Single(_links.Provisioned);
        var (linkId, recipients, oneTime) = _links.Provisioned[0];
        Assert.Equal(id, linkId);
        Assert.Equal(2, recipients.Count);
        Assert.True(oneTime);
        Assert.Contains(recipients, r => r.Recipient == "alice@example.com");

        // Minted codes are shown as full short URLs built from the configured base.
        var minted = cut.Find("[data-testid=minted]");
        Assert.Contains("https://s.example/", minted.InnerHtml);
    }

    [Fact]
    public void Provision_EmptyInput_ShowsError_DoesNotCallService()
    {
        var id = SeedUserAttributedLink();
        var cut = Render<LinkDetail>(p => p.Add(c => c.Id, id));

        cut.Find("[data-testid=provision-submit]").Click();

        Assert.Empty(_links.Provisioned);
        Assert.NotNull(cut.Find("[data-testid=provision-error]"));
    }

    [Fact]
    public void Pin_SelectingVerifiedDomain_CallsSetLinkDomain()
    {
        var id = SeedUserAttributedLink();
        var domainId = SeedVerifiedDomain();
        var cut = Render<LinkDetail>(p => p.Add(c => c.Id, id));

        cut.Find("[data-testid=pin-select]").Change(domainId.ToString());
        cut.Find("[data-testid=pin-save]").Click();

        Assert.Single(_links.DomainSet);
        Assert.Equal(id, _links.DomainSet[0].LinkId);
        Assert.Equal(domainId, _links.DomainSet[0].DomainId);
        Assert.Equal(_accountId, _links.DomainSet[0].AccountId);
    }

    [Fact]
    public void Campaign_SelectingCampaign_CallsSetLinkCampaign()
    {
        var id = SeedUserAttributedLink();
        var campaignId = SeedCampaign();
        var cut = Render<LinkDetail>(p => p.Add(c => c.Id, id));

        cut.Find("[data-testid=campaign-select]").Change(campaignId.ToString());
        cut.Find("[data-testid=campaign-save]").Click();

        Assert.Single(_links.CampaignSet);
        Assert.Equal(id, _links.CampaignSet[0].LinkId);
        Assert.Equal(campaignId, _links.CampaignSet[0].CampaignId);
        Assert.Equal(_accountId, _links.CampaignSet[0].AccountId);
    }

    private Guid SeedCampaign(string name = "Launch")
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        var c = new CampaignEntity
        {
            Id = Guid.CreateVersion7(), AccountId = _accountId, Name = name, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.CampaignEntities.Add(c);
        db.SaveChanges();
        return c.Id;
    }

    [Fact]
    public void QrCard_RendersPngAndSvgDownloadLinks_ForAnonymousLink()
    {
        var id = SeedAnonymousLink();
        var cut = Render<LinkDetail>(p => p.Add(c => c.Id, id));

        Assert.Equal($"/qr/{id}?format=png", cut.Find("[data-testid=qr-png]").GetAttribute("href"));
        Assert.Equal($"/qr/{id}?format=svg", cut.Find("[data-testid=qr-svg]").GetAttribute("href"));
        Assert.NotNull(cut.Find("[data-testid=qr-preview]"));
    }

    [Fact]
    public void Breakdown_RendersUniqueSourceAndDevice_ForAnonymousLink()
    {
        var id = SeedAnonymousLinkWithVisits();
        var cut = Render<LinkDetail>(p => p.Add(c => c.Id, id));

        Assert.NotNull(cut.Find("[data-testid=click-breakdown]"));
        Assert.Equal("3", cut.Find("[data-testid=total-clicks]").TextContent.Trim());
        Assert.Equal("2", cut.Find("[data-testid=unique-clicks]").TextContent.Trim()); // ip1, ip2
        Assert.Contains("Twitter", cut.Find("[data-testid=sources]").InnerHtml);
        Assert.Contains("Mobile", cut.Find("[data-testid=devices]").InnerHtml);
        Assert.NotNull(cut.Find("[data-testid=timeline]"));
        // Range selector renders the four windows.
        Assert.NotNull(cut.Find("[data-testid=range-7]"));
        Assert.NotNull(cut.Find("[data-testid=range-30]"));
        // Per-bar hover tooltip carries the date plus that day's total and unique counts (3 total, 2 unique).
        Assert.NotEmpty(cut.FindAll("[data-testid=bar-tooltip]"));
        Assert.Contains("Total clicks: 3", cut.Markup);
        Assert.Contains("Unique clicks: 2", cut.Markup);
        // New Phase 0.5 dimensions surface in the breakdown card...
        Assert.Contains("Safari", cut.Find("[data-testid=browsers]").InnerHtml);
        Assert.Contains("iOS", cut.Find("[data-testid=operating-systems]").InnerHtml);
        Assert.Contains("en", cut.Find("[data-testid=languages]").InnerHtml);
        // ...and the clicks table gains a sortable Browser column.
        Assert.NotNull(cut.Find("[data-testid=sort-Browser]"));
        Assert.Contains("Chrome", cut.Find("[data-testid=clicks-table]").InnerHtml);
    }

    [Fact]
    public void ShortUrl_RendersFullUrl_AndCopyButton_ForAnonymousLink()
    {
        var id = SeedAnonymousLink(); // code "abc12345"
        var cut = Render<LinkDetail>(p => p.Add(c => c.Id, id));

        var link = cut.Find("[data-testid=short-url]");
        Assert.Equal("https://s.example/abc12345", link.GetAttribute("href"));
        Assert.Contains("https://s.example/abc12345", link.TextContent);
        Assert.NotNull(cut.Find("[data-testid=copy-short-url]"));
    }

    private Guid SeedAnonymousLinkWithVisits()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        var link = new LinkEntity
        {
            Id = Guid.CreateVersion7(), OriginalUrl = "https://example.com", Mode = LinkMode.Anonymous,
            AccountId = _accountId, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
        };
        var sc = new ShortCodeEntity
        {
            Id = Guid.CreateVersion7(), LinkId = link.Id, Code = "vis12345",
            CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
        };
        db.LinkEntities.Add(link);
        db.ShortCodeEntities.Add(sc);
        db.VisitEntities.AddRange(
            Visit(sc.Id, "ip1", ClickSource.Twitter, DeviceType.Mobile, "Safari", "iOS", "en"),
            Visit(sc.Id, "ip1", ClickSource.Twitter, DeviceType.Mobile, "Safari", "iOS", "en"),
            Visit(sc.Id, "ip2", ClickSource.Direct, DeviceType.Desktop, "Chrome", "Windows", "fr"));
        db.SaveChanges();
        return link.Id;

        static VisitEntity Visit(Guid scId, string ip, ClickSource source, DeviceType device,
            string? browser = null, string? os = null, string? language = null) => new()
        {
            Id = Guid.CreateVersion7(), ShortCodeId = scId, HashedIp = ip,
            Source = source, Device = device, ClickedAt = DateTimeOffset.UtcNow,
            Browser = browser, Os = os, Language = language,
        };
    }

    private Guid SeedAnonymousLink()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        var link = new LinkEntity
        {
            Id = Guid.CreateVersion7(),
            OriginalUrl = "https://example.com",
            Mode = LinkMode.Anonymous,
            AccountId = _accountId,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        };
        db.LinkEntities.Add(link);
        db.ShortCodeEntities.Add(new ShortCodeEntity
        {
            Id = Guid.CreateVersion7(), LinkId = link.Id, Code = "abc12345",
            CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
        });
        db.SaveChanges();
        return link.Id;
    }

    private Guid SeedVerifiedDomain(string domain = "go.example.com")
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        var d = new CustomDomainEntity
        {
            Id = Guid.CreateVersion7(),
            AccountId = _accountId,
            Domain = domain,
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
            VerificationStatus = DomainVerificationStatus.Verified,
            VerificationToken = "tok",
            VerifiedAt = DateTimeOffset.UtcNow,
        };
        db.CustomDomainEntities.Add(d);
        db.SaveChanges();
        return d.Id;
    }
}
