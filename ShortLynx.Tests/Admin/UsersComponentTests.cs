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
using ShortLynx.Services.Users;

namespace ShortLynx.Tests.Admin;

public class UsersComponentTests : BunitContext
{
    private readonly FakeUserAdminService _users = new();
    private readonly SqliteConnection _conn;
    private readonly Guid _accountId = Guid.CreateVersion7();

    public UsersComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<IUserAdminService>(_ => _users);

        var auth = AddAuthorization();
        auth.SetAuthorized("admin@example.com");
        auth.SetPolicies(ShortLynx.Admin.Options.AdminClaims.SuperAdminPolicy);
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, Guid.CreateVersion7().ToString()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        db.Add(new AccountEntity { Id = _accountId, Name = "Acme Inc.", CreatedAt = DateTimeOffset.UtcNow, IsActive = true });
        db.SaveChanges();
    }

    [Fact]
    public void AddUser_NewAccount_CallsServiceWithNullAccount()
    {
        var cut = Render<Users>();
        cut.Find("button.btn-primary").Click();                          // + Add user
        cut.Find("[data-testid=add-email]").Change("new@example.com");
        cut.Find("[data-testid=add-account-name]").Change("Globex");
        cut.Find("[data-testid=add-submit]").Click();

        var added = Assert.Single(_users.Added);
        Assert.Equal("new@example.com", added.Email);
        Assert.Null(added.AccountId);
        Assert.Equal("Globex", added.Name);
    }

    [Fact]
    public void AddUser_ExistingAccount_PassesAccountAndRole()
    {
        var cut = Render<Users>();
        cut.Find("button.btn-primary").Click();
        cut.Find("[data-testid=add-email]").Change("teammate@example.com");
        cut.Find("[data-testid=add-account]").Change(_accountId.ToString());   // pick existing account
        cut.Find("[data-testid=add-role]").Change(nameof(AccountRole.Admin));
        cut.Find("[data-testid=add-submit]").Click();

        var added = Assert.Single(_users.Added);
        Assert.Equal(_accountId, added.AccountId);
        Assert.Equal(AccountRole.Admin, added.Role);
    }

    [Fact]
    public void ToggleSuperAdmin_CallsService()
    {
        var uid = Guid.CreateVersion7();
        _users.Users.Add(new AdminUserView(uid, "u@example.com", true, false, DateTimeOffset.UtcNow, []));

        var cut = Render<Users>();
        cut.Find("[data-testid=toggle-admin]").Click();

        var set = Assert.Single(_users.SuperAdminSet);
        Assert.Equal(uid, set.UserId);
        Assert.True(set.Value);
    }

    [Fact]
    public void Deactivate_CallsServiceWithFalse()
    {
        var uid = Guid.CreateVersion7();
        _users.Users.Add(new AdminUserView(uid, "u@example.com", true, false, DateTimeOffset.UtcNow, []));

        var cut = Render<Users>();
        cut.Find("[data-testid=toggle-active]").Click();

        var set = Assert.Single(_users.ActiveSet);
        Assert.Equal(uid, set.UserId);
        Assert.False(set.Value);
    }
}
