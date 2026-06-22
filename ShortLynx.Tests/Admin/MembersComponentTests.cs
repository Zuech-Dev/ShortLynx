using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Admin.Components.Pages;
using ShortLynx.Data.Context;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;

namespace ShortLynx.Tests.Admin;

public class MembersComponentTests : BunitContext
{
    private readonly FakeAccountService _accounts = new();
    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();
    private readonly Guid _accountId;

    public MembersComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ShortLynxDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>().CreateDbContext());
        Services.AddScoped<IAccountService>(_ => _accounts);

        var auth = AddAuthorization();
        auth.SetAuthorized("owner@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, _uid.ToString()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        _accountId = AccountTestSeed.SeedOwner(db, _uid);
    }

    [Fact]
    public void Owner_SeesInviteControl_AndInvites()
    {
        _accounts.Role = AccountRole.Owner;
        _accounts.Members.Add(new MemberView(_uid, "owner@example.com", AccountRole.Owner, DateTimeOffset.UtcNow));

        var cut = Render<Members>();
        cut.Find("button.btn-primary").Click();                                  // + Invite member
        cut.Find("[data-testid=invite-email]").Change("teammate@example.com");
        cut.Find("[data-testid=invite-submit]").Click();

        Assert.Single(_accounts.Invited);
        Assert.Equal("teammate@example.com", _accounts.Invited[0].Email);
        Assert.Equal(_accountId, _accounts.Invited[0].AccountId);
        Assert.Equal(_uid, _accounts.Invited[0].By);
    }

    [Fact]
    public void Member_DoesNotSeeInviteControl()
    {
        _accounts.Role = AccountRole.Member;
        _accounts.Members.Add(new MemberView(_uid, "me@example.com", AccountRole.Member, DateTimeOffset.UtcNow));

        var cut = Render<Members>();

        Assert.DoesNotContain("Invite member", cut.Markup);
    }

    [Fact]
    public void Owner_CanRemoveLowerRoleMember()
    {
        var memberId = Guid.CreateVersion7();
        _accounts.Role = AccountRole.Owner;
        _accounts.Members.Add(new MemberView(_uid, "owner@example.com", AccountRole.Owner, DateTimeOffset.UtcNow));
        _accounts.Members.Add(new MemberView(memberId, "member@example.com", AccountRole.Member, DateTimeOffset.UtcNow));

        var cut = Render<Members>();
        cut.Find("[data-testid=remove-btn]").Click();

        Assert.Single(_accounts.Removed);
        Assert.Equal(memberId, _accounts.Removed[0].Target);
    }
}
