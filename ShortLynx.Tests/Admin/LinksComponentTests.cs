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

    public LinksComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ILinkService>(_ => _links);

        var auth = AddAuthorization();
        auth.SetAuthorized("user@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, _uid.ToString()));

        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public void CreateLink_Valid_ShowsShortCode_AndCallsServiceWithUser()
    {
        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click();                       // + New link
        cut.Find("input.form-control").Change("https://example.com"); // URL
        cut.Find("form").Submit();

        Assert.Single(_links.Created);
        Assert.Equal(_uid, _links.Created[0].Uid);
        Assert.Contains(_links.CodeToReturn, cut.Markup);
        Assert.NotNull(cut.Find("[data-testid=new-link]"));
    }

    [Fact]
    public void CreateLink_ServiceRejectsUrl_ShowsError_NoLinkCreated()
    {
        _links.ThrowOnCreate = true;

        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click();
        cut.Find("input.form-control").Change("https://blocked.example.com");
        cut.Find("form").Submit();

        Assert.Empty(_links.Created);
        Assert.Contains("blocked URL", cut.Markup);
    }

    [Fact]
    public void CreateLink_UserAttributedMode_CallsMode2Path()
    {
        var cut = Render<Links>();
        cut.Find("button.btn-primary").Click();                       // + New link
        cut.Find("input.form-control").Change("https://example.com"); // URL
        cut.Find("#mode-user").Change("UserAttributed");              // switch to user-attributed
        cut.Find("form").Submit();

        // Routed through the Mode-2 creation path, not the anonymous one.
        Assert.Empty(_links.Created);
        Assert.Single(_links.CreatedUserAttributed);
        Assert.Equal(_uid, _links.CreatedUserAttributed[0].Uid);
    }
}
