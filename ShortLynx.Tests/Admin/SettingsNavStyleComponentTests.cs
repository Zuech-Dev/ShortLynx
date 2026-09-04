using Bunit;
using Bunit.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Admin.Components.Pages;
using ShortLynx.Admin.Services;
using ShortLynx.Data.Context;

namespace ShortLynx.Tests.Admin;

// The nav-style buttons on /settings and MainLayout's own rendering share one NavPreferenceService
// instance per circuit (registered Scoped) — a click here must update that shared instance directly,
// which is what lets the live nav bar swap styles without a page reload.
public class SettingsNavStyleComponentTests : BunitContext
{
    private readonly SqliteConnection _conn;

    public SettingsNavStyleComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<NavPreferenceService>();

        var auth = AddAuthorization();
        auth.SetAuthorized("user@example.com");
        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public void HamburgerIsSelected_ByDefault()
    {
        var cut = Render<Settings>();

        Assert.Equal("true", cut.Find("[data-testid=nav-style-hamburger]").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find("[data-testid=nav-style-scroll]").GetAttribute("aria-pressed"));
    }

    [Fact]
    public void ClickingHorizontalScroll_UpdatesSelection_AndSharedPreference()
    {
        var cut = Render<Settings>();

        cut.Find("[data-testid=nav-style-scroll]").Click();

        Assert.Equal("true", cut.Find("[data-testid=nav-style-scroll]").GetAttribute("aria-pressed"));
        Assert.Equal("false", cut.Find("[data-testid=nav-style-hamburger]").GetAttribute("aria-pressed"));
        Assert.Equal(NavStyle.HorizontalScroll, Services.GetRequiredService<NavPreferenceService>().Style);
    }

    protected override void Dispose(bool disposing)
    {
        _conn.Dispose();
        base.Dispose(disposing);
    }
}
