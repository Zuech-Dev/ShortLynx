using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ShortLynx.Admin.Pages.Auth;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Auth;
using ShortLynx.Services.MagicLinks;

namespace ShortLynx.Tests.Admin;

public class ConfirmModelTests
{
    private sealed class StubMagicLinkService(UserAccountEntity? user) : IMagicLinkService
    {
        public Task<string> CreateTokenAsync(string email, CancellationToken ct = default) => Task.FromResult("t");
        public Task<UserAccountEntity?> ValidateTokenAsync(string token, CancellationToken ct = default)
            => Task.FromResult(user);
    }

    private sealed class StubDbContextFactory(SqliteConnection conn) : IDbContextFactory<ShortLynxDbContext>
    {
        public ShortLynxDbContext CreateDbContext()
            => new(new DbContextOptionsBuilder<ShortLynxDbContext>().UseSqlite(conn).Options);
    }

    private static (ConfirmModel Model, DefaultHttpContext Http) MakeModel(
        UserAccountEntity user, FakeAccountService accounts, AccessControlOptions opts)
    {
        var conn = new SqliteConnection("DataSource=:memory:");
        conn.Open();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        services.AddAuthorization();
        var sp = services.BuildServiceProvider();

        var http = new DefaultHttpContext { RequestServices = sp };
        var model = new ConfirmModel(
            new StubMagicLinkService(user),
            new StubDbContextFactory(conn),
            accounts,
            Options.Create(opts))
        {
            PageContext = new PageContext { HttpContext = http },
        };
        return (model, http);
    }

    private static UserAccountEntity User(string email) => new()
    {
        Id = Guid.CreateVersion7(), Email = email, CreatedAt = DateTimeOffset.UtcNow, IsActive = true, IsAdmin = false,
    };

    [Fact]
    public async Task AllowlistedUser_WithNoMembership_SignsIn()
    {
        var user = User("allowed@example.com");
        var opts = new AccessControlOptions { AllowedEmails = ["allowed@example.com"] };
        var (model, _) = MakeModel(user, new FakeAccountService { Members = { } }, opts);

        var result = await model.OnGetAsync("token", null, default);

        Assert.IsType<LocalRedirectResult>(result);
        Assert.Empty(model.Error);
    }

    [Fact]
    public async Task UnlistedUser_WithNoMembership_IsDenied()
    {
        var user = User("stranger@example.com");
        var accounts = new FakeAccountService();
        // No accounts for this user.
        accounts.AccountsFor = _ => [];
        var (model, _) = MakeModel(user, accounts, new AccessControlOptions());

        var result = await model.OnGetAsync("token", null, default);

        Assert.IsType<PageResult>(result);
        Assert.NotEmpty(model.Error);
    }

    [Fact]
    public async Task UnlistedUser_WithMembership_SignsIn()
    {
        var user = User("member@example.com");
        var accounts = new FakeAccountService();
        accounts.AccountsFor = _ => [new AccountSummary(Guid.CreateVersion7(), "Acme", AccountRole.Member)];
        var (model, _) = MakeModel(user, accounts, new AccessControlOptions());

        var result = await model.OnGetAsync("token", null, default);

        Assert.IsType<LocalRedirectResult>(result);
        Assert.Empty(model.Error);
    }
}
