using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Admin.Components.Pages;
using ShortLynx.Data.Context;
using ShortLynx.Data.Enums;

namespace ShortLynx.Tests.Admin;

/// <summary>
/// Regression cover for the "Save button did nothing" report on the Settings privacy panel.
/// Two failure modes are fixed: the confirmation checkbox was hidden until the URL committed on
/// blur (so it gated the save invisibly), and a non-owner saw only a silently-disabled button.
/// </summary>
public class SettingsSaveTests : BunitContext
{
    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();
    private Guid _accountId;

    private void Setup(AccountRole role)
    {
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ShortLynxDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>().CreateDbContext());
        Services.AddScoped<ShortLynx.Services.Accounts.IAccountService, ShortLynx.Services.Accounts.AccountService>();

        var auth = AddAuthorization();
        auth.SetAuthorized("owner@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, _uid.ToString()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        _accountId = AccountTestSeed.Seed(db, _uid, role, "owner@example.com");
    }

    public SettingsSaveTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
    }

    private string? SavedUrl()
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        return db.AccountEntities.Single(a => a.Id == _accountId).PrivacyPolicyUrl;
    }

    [Fact]
    public void Confirmation_Appears_AsSoonAsUrlIsTyped()
    {
        Setup(AccountRole.Owner);
        var cut = Render<Settings>();

        Assert.Empty(cut.FindAll("[data-testid=privacy-confirm]")); // nothing typed yet
        cut.Find("[data-testid=privacy-url]").Input("https://shrtlynx.com/privacy"); // per-keystroke bind
        Assert.NotEmpty(cut.FindAll("[data-testid=privacy-confirm]")); // now visible — not a hidden gate
    }

    [Fact]
    public void Owner_TypeUrl_Confirm_Save_Persists()
    {
        Setup(AccountRole.Owner);
        var cut = Render<Settings>();

        cut.Find("[data-testid=privacy-url]").Input("https://shrtlynx.com/privacy");
        cut.Find("[data-testid=privacy-confirm]").Change(true);
        cut.Find("[data-testid=privacy-save]").Click();

        Assert.Equal("https://shrtlynx.com/privacy", SavedUrl());
    }

    [Fact]
    public void Owner_Save_WithoutConfirm_ShowsErrorAndDoesNotPersist()
    {
        Setup(AccountRole.Owner);
        var cut = Render<Settings>();

        cut.Find("[data-testid=privacy-url]").Input("https://shrtlynx.com/privacy");
        cut.Find("[data-testid=privacy-save]").Click();

        Assert.NotEmpty(cut.FindAll("[data-testid=privacy-error]")); // visible feedback, not silent
        Assert.Null(SavedUrl());
    }

    [Theory]
    [InlineData(AccountRole.Viewer)]
    [InlineData(AccountRole.Member)]
    [InlineData(AccountRole.Admin)]
    public void NonOwner_SeesPermissionNotice_AndCannotSave(AccountRole role)
    {
        Setup(role);
        var cut = Render<Settings>();

        // Boundary is visible (not a silently-disabled button), and the handler guard holds.
        Assert.NotEmpty(cut.FindAll("[data-testid=privacy-noperm]"));
        Assert.True(cut.Find("[data-testid=privacy-save]").HasAttribute("disabled"));

        cut.Find("[data-testid=privacy-url]").Input("https://evil.example.com/policy");
        cut.Find("[data-testid=privacy-save]").Click(); // bUnit clicks even when disabled
        Assert.Null(SavedUrl());
    }

    [Fact]
    public void Owner_HasNoPermissionNotice()
    {
        Setup(AccountRole.Owner);
        var cut = Render<Settings>();
        Assert.Empty(cut.FindAll("[data-testid=privacy-noperm]"));
        Assert.False(cut.Find("[data-testid=privacy-save]").HasAttribute("disabled"));
    }

    protected override void Dispose(bool disposing)
    {
        _conn.Dispose();
        base.Dispose(disposing);
    }
}
