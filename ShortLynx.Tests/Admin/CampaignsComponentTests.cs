using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Admin.Components.Pages;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Campaigns;

namespace ShortLynx.Tests.Admin;

public class CampaignsComponentTests : BunitContext
{
    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();
    private readonly Guid _accountId;

    public CampaignsComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ShortLynxDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>().CreateDbContext());
        Services.AddScoped<ICampaignService, CampaignService>();

        var auth = AddAuthorization();
        auth.SetAuthorized("user@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, _uid.ToString()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        _accountId = AccountTestSeed.SeedOwner(db, _uid);
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
    public void Campaigns_EmptyState_RendersWhenNoneExist()
    {
        var cut = Render<Campaigns>();
        cut.WaitForElement("[data-testid=empty]");
        Assert.NotNull(cut.Find("[data-testid=empty]"));
    }

    [Fact]
    public void Campaigns_Create_AddsCampaignToList()
    {
        var cut = Render<Campaigns>();

        cut.Find("[data-testid=new-campaign]").Click();
        cut.Find("[data-testid=name-input]").Change("Spring Launch");
        cut.Find("[data-testid=add-submit]").Click();

        var row = cut.WaitForElement("[data-testid=campaign-row]");
        Assert.Contains("Spring Launch", row.InnerHtml);

        // Persisted to the account.
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        Assert.True(db.CampaignEntities.Any(c => c.Name == "Spring Launch" && c.AccountId == _accountId));
    }

    [Fact]
    public void Campaigns_ListsExistingWithLinkCount()
    {
        var campaignId = SeedCampaign("Existing");
        // Assign a link to it.
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            db.LinkEntities.Add(new LinkEntity
            {
                Id = Guid.CreateVersion7(), OriginalUrl = "https://example.com", Mode = LinkMode.Anonymous,
                AccountId = _accountId, CampaignId = campaignId, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            });
            db.SaveChanges();
        }

        var cut = Render<Campaigns>();
        var count = cut.WaitForElement("[data-testid=link-count]");
        Assert.Contains("1 link", count.TextContent);
    }
}

public class CampaignDetailComponentTests : BunitContext
{
    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();
    private readonly Guid _accountId;

    public CampaignDetailComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ShortLynxDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>().CreateDbContext());
        Services.AddScoped<ICampaignService, CampaignService>();

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
    public void CampaignDetail_RendersRollupAndPerLinkTable()
    {
        Guid campaignId;
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            var campaign = new CampaignEntity
            {
                Id = Guid.CreateVersion7(), AccountId = _accountId, Name = "Launch", CreatedAt = DateTimeOffset.UtcNow,
            };
            var link = new LinkEntity
            {
                Id = Guid.CreateVersion7(), OriginalUrl = "https://example.com", Mode = LinkMode.Anonymous,
                AccountId = _accountId, CampaignId = campaign.Id, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            };
            var sc = new ShortCodeEntity
            {
                Id = Guid.CreateVersion7(), LinkId = link.Id, Code = "cmp12345",
                CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            };
            db.AddRange(campaign, link, sc);
            db.VisitEntities.AddRange(
                Visit(sc.Id, "ip1", ClickSource.Twitter, DeviceType.Mobile),
                Visit(sc.Id, "ip1", ClickSource.Twitter, DeviceType.Mobile),
                Visit(sc.Id, "ip2", ClickSource.Direct, DeviceType.Desktop));
            db.SaveChanges();
            campaignId = campaign.Id;
        }

        var cut = Render<CampaignDetail>(p => p.Add(c => c.Id, campaignId));

        cut.WaitForElement("[data-testid=click-breakdown]");
        Assert.Equal("3", cut.Find("[data-testid=total-clicks]").TextContent.Trim());
        Assert.Equal("2", cut.Find("[data-testid=unique-clicks]").TextContent.Trim());
        Assert.Contains("Twitter", cut.Find("[data-testid=sources]").InnerHtml);
        Assert.NotNull(cut.Find("[data-testid=link-table]"));
    }

    [Fact]
    public void CampaignDetail_ForeignCampaign_ShowsNotFound()
    {
        // A campaign owned by a different account.
        Guid otherId;
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            var otherAccount = new AccountEntity
            {
                Id = Guid.CreateVersion7(), Name = "Other Co", CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            };
            var c = new CampaignEntity
            {
                Id = Guid.CreateVersion7(), AccountId = otherAccount.Id, Name = "Theirs",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.AddRange(otherAccount, c);
            db.SaveChanges();
            otherId = c.Id;
        }

        var cut = Render<CampaignDetail>(p => p.Add(c => c.Id, otherId));
        cut.WaitForState(() => cut.Markup.Contains("not found", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("not found", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }

    private static VisitEntity Visit(Guid scId, string ip, ClickSource source, DeviceType device) => new()
    {
        Id = Guid.CreateVersion7(), ShortCodeId = scId, HashedIp = ip,
        Source = source, Device = device, ClickedAt = DateTimeOffset.UtcNow,
    };
}
