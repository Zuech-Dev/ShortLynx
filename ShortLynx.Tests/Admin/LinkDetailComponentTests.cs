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
    }

    private Guid SeedUserAttributedLink()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.UserAccountEntities.Add(new UserAccountEntity
        {
            Id = _uid,
            Email = "user@example.com",
            CreatedAt = DateTimeOffset.UtcNow,
            IsActive = true,
        });
        var link = new LinkEntity
        {
            Id = Guid.CreateVersion7(),
            OriginalUrl = "https://example.com",
            Mode = LinkMode.UserAttributed,
            UserAccountId = _uid,
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
}
