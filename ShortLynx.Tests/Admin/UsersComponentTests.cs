using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Admin.Components.Pages;
using ShortLynx.Data.Context;
using ShortLynx.Services.Accounts;

namespace ShortLynx.Tests.Admin;

public class UsersComponentTests : BunitContext
{
    private readonly FakeAccountService _accounts = new();
    private readonly SqliteConnection _conn;

    public UsersComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<IAccountService>(_ => _accounts);

        var auth = AddAuthorization();
        auth.SetAuthorized("admin@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    [Fact]
    public void CreateAccount_CallsService_AndShowsConfirmation()
    {
        var cut = Render<Users>();
        cut.Find("button.btn-primary").Click();                            // + New account
        cut.Find("[data-testid=account-name]").Change("Acme Inc.");
        cut.Find("[data-testid=owner-email]").Change("owner@example.com");
        cut.Find("[data-testid=create-account-submit]").Click();

        Assert.Single(_accounts.CreatedAccounts);
        Assert.Equal("Acme Inc.", _accounts.CreatedAccounts[0].Name);
        Assert.Equal("owner@example.com", _accounts.CreatedAccounts[0].OwnerEmail);
        Assert.NotNull(cut.Find("[data-testid=account-created]"));
    }
}
