using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.MagicLinks;

namespace ShortLynx.Tests.Services.Accounts;

public class AccountServiceTests
{
    private sealed class FakeMagicLinkService : IMagicLinkService
    {
        public readonly List<string> Sent = [];
        public Task<string> CreateTokenAsync(string email, CancellationToken ct = default)
        {
            Sent.Add(email.Trim().ToLowerInvariant());
            return Task.FromResult("token");
        }
        public Task<UserAccountEntity?> ValidateTokenAsync(string token, CancellationToken ct = default)
            => Task.FromResult<UserAccountEntity?>(null);
    }

    private static AccountService MakeSvc(ShortLynxDbContext ctx, FakeMagicLinkService? magic = null)
        => new(ctx, magic ?? new FakeMagicLinkService());

    // Seeds an account with a member at the given role, returning (accountId, userId).
    private static async Task<(Guid AccountId, Guid UserId)> SeedMemberAsync(
        TestDatabase db, AccountRole role, string email)
    {
        var account = EntityFactory.Account();
        var user = EntityFactory.UserAccount(email);
        await using var ctx = db.CreateContext();
        ctx.AddRange(account, user);
        ctx.MembershipEntities.Add(EntityFactory.Membership(account.Id, user.Id, role));
        await ctx.SaveChangesAsync();
        return (account.Id, user.Id);
    }

    private static async Task<Guid> AddMemberAsync(TestDatabase db, Guid accountId, AccountRole role, string email)
    {
        var user = EntityFactory.UserAccount(email);
        await using var ctx = db.CreateContext();
        ctx.UserAccountEntities.Add(user);
        ctx.MembershipEntities.Add(EntityFactory.Membership(accountId, user.Id, role));
        await ctx.SaveChangesAsync();
        return user.Id;
    }

    // ── CreateAccountWithOwner ────────────────────────────────────────────────

    [Fact]
    public async Task CreateAccountWithOwner_CreatesAccount_OwnerMembership_AndSendsLink()
    {
        await using var db = await TestDatabase.CreateAsync();
        var magic = new FakeMagicLinkService();

        var account = await MakeSvc(db.CreateContext(), magic).CreateAccountWithOwnerAsync("Acme", "owner@example.com");

        await using var ctx = db.CreateContext();
        var membership = await ctx.MembershipEntities.Include(m => m.UserAccount)
            .SingleAsync(m => m.AccountId == account.Id);
        Assert.Equal(AccountRole.Owner, membership.Role);
        Assert.Equal("owner@example.com", membership.UserAccount.Email);
        Assert.Contains("owner@example.com", magic.Sent);
    }

    [Fact]
    public async Task CreateAccountWithOwner_ReusesExistingUser()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using (var seed = db.CreateContext())
        {
            seed.UserAccountEntities.Add(EntityFactory.UserAccount("dup@example.com"));
            await seed.SaveChangesAsync();
        }

        await MakeSvc(db.CreateContext()).CreateAccountWithOwnerAsync("Acme", "DUP@example.com");

        await using var ctx = db.CreateContext();
        Assert.Equal(1, await ctx.UserAccountEntities.CountAsync(u => u.Email == "dup@example.com"));
    }

    // ── InviteMember ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Invite_OwnerInvitesMember_CreatesMembership_AndSendsLink()
    {
        await using var db = await TestDatabase.CreateAsync();
        var magic = new FakeMagicLinkService();
        var (accountId, ownerId) = await SeedMemberAsync(db, AccountRole.Owner, "owner@example.com");

        var membership = await MakeSvc(db.CreateContext(), magic)
            .InviteMemberAsync(accountId, "member@example.com", AccountRole.Member, ownerId);

        Assert.Equal(AccountRole.Member, membership.Role);
        Assert.Equal(ownerId, membership.InvitedByUserAccountId);
        Assert.Contains("member@example.com", magic.Sent);
    }

    [Fact]
    public async Task Invite_ByPlainMember_Throws()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, memberId) = await SeedMemberAsync(db, AccountRole.Member, "member@example.com");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            MakeSvc(db.CreateContext()).InviteMemberAsync(accountId, "new@example.com", AccountRole.Member, memberId));
    }

    [Fact]
    public async Task Invite_NonMemberActor_Throws()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, _) = await SeedMemberAsync(db, AccountRole.Owner, "owner@example.com");

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            MakeSvc(db.CreateContext()).InviteMemberAsync(accountId, "new@example.com", AccountRole.Member, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task Invite_AdminCannotGrantAdmin_Throws()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, _) = await SeedMemberAsync(db, AccountRole.Owner, "owner@example.com");
        var adminId = await AddMemberAsync(db, accountId, AccountRole.Admin, "admin@example.com");

        await Assert.ThrowsAsync<ArgumentException>(() =>
            MakeSvc(db.CreateContext()).InviteMemberAsync(accountId, "new@example.com", AccountRole.Admin, adminId));
    }

    [Fact]
    public async Task Invite_AlreadyMember_Throws()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, ownerId) = await SeedMemberAsync(db, AccountRole.Owner, "owner@example.com");
        await AddMemberAsync(db, accountId, AccountRole.Member, "member@example.com");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MakeSvc(db.CreateContext()).InviteMemberAsync(accountId, "member@example.com", AccountRole.Member, ownerId));
    }

    // ── ChangeRole ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ChangeRole_OwnerPromotesMember_ToAdmin()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, ownerId) = await SeedMemberAsync(db, AccountRole.Owner, "owner@example.com");
        var memberId = await AddMemberAsync(db, accountId, AccountRole.Member, "member@example.com");

        var ok = await MakeSvc(db.CreateContext()).ChangeRoleAsync(accountId, memberId, AccountRole.Admin, ownerId);
        Assert.True(ok);

        await using var ctx = db.CreateContext();
        var m = await ctx.MembershipEntities.SingleAsync(x => x.AccountId == accountId && x.UserAccountId == memberId);
        Assert.Equal(AccountRole.Admin, m.Role);
    }

    [Fact]
    public async Task ChangeRole_AdminCannotChangeAnotherAdmin()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, _) = await SeedMemberAsync(db, AccountRole.Owner, "owner@example.com");
        var admin1 = await AddMemberAsync(db, accountId, AccountRole.Admin, "admin1@example.com");
        var admin2 = await AddMemberAsync(db, accountId, AccountRole.Admin, "admin2@example.com");

        var ok = await MakeSvc(db.CreateContext()).ChangeRoleAsync(accountId, admin2, AccountRole.Member, admin1);
        Assert.False(ok);
    }

    [Fact]
    public async Task ChangeRole_SoleOwnerCannotDemoteSelf()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, ownerId) = await SeedMemberAsync(db, AccountRole.Owner, "owner@example.com");

        var ok = await MakeSvc(db.CreateContext()).ChangeRoleAsync(accountId, ownerId, AccountRole.Member, ownerId);
        Assert.False(ok);
    }

    // ── RemoveMember ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveMember_OwnerRemovesMember()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, ownerId) = await SeedMemberAsync(db, AccountRole.Owner, "owner@example.com");
        var memberId = await AddMemberAsync(db, accountId, AccountRole.Member, "member@example.com");

        var ok = await MakeSvc(db.CreateContext()).RemoveMemberAsync(accountId, memberId, ownerId);
        Assert.True(ok);

        await using var ctx = db.CreateContext();
        Assert.False(await ctx.MembershipEntities.AnyAsync(m => m.AccountId == accountId && m.UserAccountId == memberId));
    }

    [Fact]
    public async Task RemoveMember_CannotRemoveEqualOrHigherRole()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, _) = await SeedMemberAsync(db, AccountRole.Owner, "owner@example.com");
        var admin = await AddMemberAsync(db, accountId, AccountRole.Admin, "admin@example.com");
        var member = await AddMemberAsync(db, accountId, AccountRole.Member, "member@example.com");

        // Member can't manage members at all → can't remove the admin.
        var ok = await MakeSvc(db.CreateContext()).RemoveMemberAsync(accountId, admin, member);
        Assert.False(ok);
    }

    // ── Queries ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListMembers_And_GetRole()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (accountId, ownerId) = await SeedMemberAsync(db, AccountRole.Owner, "owner@example.com");
        await AddMemberAsync(db, accountId, AccountRole.Viewer, "viewer@example.com");

        var members = await MakeSvc(db.CreateContext()).ListMembersAsync(accountId);
        Assert.Equal(2, members.Count);

        var role = await MakeSvc(db.CreateContext()).GetRoleAsync(accountId, ownerId);
        Assert.Equal(AccountRole.Owner, role);
        Assert.Null(await MakeSvc(db.CreateContext()).GetRoleAsync(accountId, Guid.CreateVersion7()));
    }

    [Fact]
    public async Task ListAccountsForUser_ReturnsAllMemberships()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (a1, userId) = await SeedMemberAsync(db, AccountRole.Owner, "multi@example.com");

        // Same user joined to a second account as Member.
        var account2 = EntityFactory.Account("Second");
        await using (var ctx = db.CreateContext())
        {
            ctx.AccountEntities.Add(account2);
            ctx.MembershipEntities.Add(EntityFactory.Membership(account2.Id, userId, AccountRole.Member));
            await ctx.SaveChangesAsync();
        }

        var accounts = await MakeSvc(db.CreateContext()).ListAccountsForUserAsync(userId);
        Assert.Equal(2, accounts.Count);
        Assert.Contains(accounts, a => a.AccountId == a1 && a.Role == AccountRole.Owner);
        Assert.Contains(accounts, a => a.AccountId == account2.Id && a.Role == AccountRole.Member);
    }
}
