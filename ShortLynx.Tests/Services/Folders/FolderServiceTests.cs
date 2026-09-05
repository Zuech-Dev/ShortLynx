using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Services.Folders;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Folders;

public class FolderServiceTests
{
    private static FolderService MakeSvc(ShortLynxDbContext ctx) => new(ctx);

    private static async Task<Guid> SeedAccountAsync(TestDatabase db)
    {
        var account = EntityFactory.Account();
        await using var ctx = db.CreateContext();
        ctx.AccountEntities.Add(account);
        await ctx.SaveChangesAsync();
        return account.Id;
    }

    [Fact]
    public async Task Create_TrimsName()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        var folder = await MakeSvc(db.CreateContext()).CreateAsync(accountId, new FolderInput("  Client X  "));

        Assert.Equal("Client X", folder.Name);
        Assert.Equal(accountId, folder.AccountId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_EmptyName_Throws(string name)
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => MakeSvc(db.CreateContext()).CreateAsync(accountId, new FolderInput(name)));
    }

    [Fact]
    public async Task List_IsAccountScoped()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);

        await MakeSvc(db.CreateContext()).CreateAsync(a, new FolderInput("A1"));
        await MakeSvc(db.CreateContext()).CreateAsync(a, new FolderInput("A2"));
        await MakeSvc(db.CreateContext()).CreateAsync(b, new FolderInput("B1"));

        var listA = await MakeSvc(db.CreateContext()).ListAsync(a);
        Assert.Equal(2, listA.Count);
        Assert.All(listA, f => Assert.Equal(a, f.AccountId));
    }

    [Fact]
    public async Task Get_ForeignAccount_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);
        var created = await MakeSvc(db.CreateContext()).CreateAsync(a, new FolderInput("A1"));

        Assert.NotNull(await MakeSvc(db.CreateContext()).GetAsync(created.Id, a));
        Assert.Null(await MakeSvc(db.CreateContext()).GetAsync(created.Id, b));
    }

    [Fact]
    public async Task Update_RenamesFolder()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var created = await MakeSvc(db.CreateContext()).CreateAsync(accountId, new FolderInput("Old"));

        var updated = await MakeSvc(db.CreateContext()).UpdateAsync(created.Id, accountId, new FolderInput("New"));

        Assert.NotNull(updated);
        Assert.Equal("New", updated!.Name);
    }

    [Fact]
    public async Task Update_ForeignAccount_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);
        var created = await MakeSvc(db.CreateContext()).CreateAsync(a, new FolderInput("A1"));

        Assert.Null(await MakeSvc(db.CreateContext()).UpdateAsync(created.Id, b, new FolderInput("hack")));
    }

    [Fact]
    public async Task Delete_RemovesFolder_AndUnassignsItsLinks()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var folder = await MakeSvc(db.CreateContext()).CreateAsync(accountId, new FolderInput("Launch"));

        Guid linkId;
        await using (var ctx = db.CreateContext())
        {
            var link = EntityFactory.AnonymousLink(accountId);
            link.FolderId = folder.Id;
            ctx.LinkEntities.Add(link);
            await ctx.SaveChangesAsync();
            linkId = link.Id;
        }

        var deleted = await MakeSvc(db.CreateContext()).DeleteAsync(folder.Id, accountId);

        Assert.True(deleted);
        await using var verify = db.CreateContext();
        Assert.False(await verify.FolderEntities.AnyAsync(f => f.Id == folder.Id));
        var survivor = await verify.LinkEntities.FirstAsync(l => l.Id == linkId);
        Assert.Null(survivor.FolderId);
    }

    [Fact]
    public async Task Delete_ForeignAccount_ReturnsFalse()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);
        var created = await MakeSvc(db.CreateContext()).CreateAsync(a, new FolderInput("A1"));

        Assert.False(await MakeSvc(db.CreateContext()).DeleteAsync(created.Id, b));
    }
}
