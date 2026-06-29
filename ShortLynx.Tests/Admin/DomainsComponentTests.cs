using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShortLynx.Admin.Components.Pages;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Domains;

namespace ShortLynx.Tests.Admin;

public class DomainsComponentTests : BunitContext
{
    private readonly FakeCustomDomainService _domains = new();
    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();
    private readonly Guid _accountId;

    public DomainsComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ShortLynxDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>().CreateDbContext());
        Services.AddScoped<ICustomDomainService>(_ => _domains);
        Services.AddSingleton<IOptions<CustomDomainOptions>>(Options.Create(new CustomDomainOptions()));

        var auth = AddAuthorization();
        auth.SetAuthorized("user@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, _uid.ToString()));

        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        _accountId = AccountTestSeed.SeedOwner(db, _uid);
    }

    private CustomDomainEntity Seed(string domain, DomainVerificationStatus status = DomainVerificationStatus.Pending)
    {
        var d = new CustomDomainEntity
        {
            Id = Guid.CreateVersion7(), AccountId = _accountId, Domain = domain,
            CreatedAt = DateTimeOffset.UtcNow, IsActive = status == DomainVerificationStatus.Verified,
            VerificationStatus = status, VerificationToken = "tok-123",
        };
        _domains.Domains.Add(d);
        return d;
    }

    [Fact]
    public void AddDomain_CallsService_AndShowsRow()
    {
        var cut = Render<Domains>();
        cut.Find("button.btn-primary").Click();                              // + Add domain
        cut.Find("[data-testid=domain-input]").Change("go.example.com");
        cut.Find("[data-testid=add-submit]").Click();

        Assert.Single(_domains.Domains);
        Assert.Equal(_uid, _domains.Domains[0].UserAccountId);
        Assert.Contains("go.example.com", cut.Markup);
    }

    [Fact]
    public void AddDomain_Duplicate_ShowsError()
    {
        _domains.ThrowOnAdd = true;

        var cut = Render<Domains>();
        cut.Find("button.btn-primary").Click();
        cut.Find("[data-testid=domain-input]").Change("dupe.example.com");
        cut.Find("[data-testid=add-submit]").Click();

        Assert.NotNull(cut.Find("[data-testid=add-error]"));
    }

    [Fact]
    public void PendingDomain_ShowsTxtInstructions_AndVerifyButton()
    {
        Seed("go.example.com");
        var cut = Render<Domains>();

        var opts = new CustomDomainOptions();
        Assert.Contains(opts.VerificationHost("go.example.com"), cut.Markup);
        Assert.NotNull(cut.Find("[data-testid=verify-btn]"));
    }

    [Fact]
    public void Verify_CallsService_AndShowsVerifiedBadge()
    {
        Seed("go.example.com");
        var cut = Render<Domains>();

        cut.Find("[data-testid=verify-btn]").Click();

        Assert.Single(_domains.Verified);
        Assert.Contains("Verified", cut.Markup);
    }

    [Fact]
    public void Remove_CallsService_AndDropsRow()
    {
        Seed("go.example.com");
        var cut = Render<Domains>();

        cut.Find("[data-testid=remove-btn]").Click();

        Assert.Single(_domains.Removed);
        Assert.DoesNotContain("data-testid=\"domain-row\"", cut.Markup);
    }
}
