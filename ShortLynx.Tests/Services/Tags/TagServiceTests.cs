using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Services.Tags;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Tags;

public class TagServiceTests
{
    private static TagService MakeSvc(ShortLynxDbContext ctx) => new(ctx);

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

        var tag = await MakeSvc(db.CreateContext()).CreateAsync(accountId, new TagInput("  urgent  "));

        Assert.Equal("urgent", tag.Name);
        Assert.Equal(accountId, tag.AccountId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_EmptyName_Throws(string name)
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => MakeSvc(db.CreateContext()).CreateAsync(accountId, new TagInput(name)));
    }

    [Fact]
    public async Task Create_DuplicateName_CaseInsensitive_Throws()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        await MakeSvc(db.CreateContext()).CreateAsync(accountId, new TagInput("Urgent"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeSvc(db.CreateContext()).CreateAsync(accountId, new TagInput("urgent")));
    }

    [Fact]
    public async Task Create_SameName_DifferentAccount_Succeeds()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);
        await MakeSvc(db.CreateContext()).CreateAsync(a, new TagInput("urgent"));

        var tag = await MakeSvc(db.CreateContext()).CreateAsync(b, new TagInput("urgent"));
        Assert.Equal("urgent", tag.Name);
    }

    [Fact]
    public async Task List_IsAccountScoped_OrderedByName()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);

        await MakeSvc(db.CreateContext()).CreateAsync(a, new TagInput("Zebra"));
        await MakeSvc(db.CreateContext()).CreateAsync(a, new TagInput("Apple"));
        await MakeSvc(db.CreateContext()).CreateAsync(b, new TagInput("B1"));

        var listA = await MakeSvc(db.CreateContext()).ListAsync(a);
        Assert.Equal(["Apple", "Zebra"], listA.Select(t => t.Name));
    }

    [Fact]
    public async Task Update_RenamesTag()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var created = await MakeSvc(db.CreateContext()).CreateAsync(accountId, new TagInput("Old"));

        var updated = await MakeSvc(db.CreateContext()).UpdateAsync(created.Id, accountId, new TagInput("New"));

        Assert.Equal("New", updated!.Name);
    }

    [Fact]
    public async Task Update_ToExistingName_Throws()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        await MakeSvc(db.CreateContext()).CreateAsync(accountId, new TagInput("Taken"));
        var toRename = await MakeSvc(db.CreateContext()).CreateAsync(accountId, new TagInput("Original"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => MakeSvc(db.CreateContext()).UpdateAsync(toRename.Id, accountId, new TagInput("Taken")));
    }

    [Fact]
    public async Task Delete_RemovesTag_AndCascadesLinkTagRows()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var tag = await MakeSvc(db.CreateContext()).CreateAsync(accountId, new TagInput("Doomed"));

        Guid linkId;
        await using (var ctx = db.CreateContext())
        {
            var link = EntityFactory.AnonymousLink(accountId);
            ctx.LinkEntities.Add(link);
            await ctx.SaveChangesAsync();
            linkId = link.Id;
        }
        await MakeSvc(db.CreateContext()).SetLinkTagsAsync(linkId, [tag.Id], accountId);

        var deleted = await MakeSvc(db.CreateContext()).DeleteAsync(tag.Id, accountId);

        Assert.True(deleted);
        await using var verify = db.CreateContext();
        Assert.False(await verify.TagEntities.AnyAsync(t => t.Id == tag.Id));
        Assert.False(await verify.LinkTagEntities.AnyAsync(lt => lt.TagId == tag.Id));
    }

    [Fact]
    public async Task SetLinkTagsAsync_FullReplace()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var tag1 = await MakeSvc(db.CreateContext()).CreateAsync(accountId, new TagInput("One"));
        var tag2 = await MakeSvc(db.CreateContext()).CreateAsync(accountId, new TagInput("Two"));

        Guid linkId;
        await using (var ctx = db.CreateContext())
        {
            var link = EntityFactory.AnonymousLink(accountId);
            ctx.LinkEntities.Add(link);
            await ctx.SaveChangesAsync();
            linkId = link.Id;
        }

        await MakeSvc(db.CreateContext()).SetLinkTagsAsync(linkId, [tag1.Id, tag2.Id], accountId);
        await using (var verify1 = db.CreateContext())
            Assert.Equal(2, await verify1.LinkTagEntities.CountAsync(lt => lt.LinkId == linkId));

        // Replacing with just tag2 removes tag1's association.
        await MakeSvc(db.CreateContext()).SetLinkTagsAsync(linkId, [tag2.Id], accountId);
        await using var verify2 = db.CreateContext();
        var remaining = await verify2.LinkTagEntities.Where(lt => lt.LinkId == linkId).ToListAsync();
        Assert.Single(remaining);
        Assert.Equal(tag2.Id, remaining[0].TagId);
    }

    [Fact]
    public async Task SetLinkTagsAsync_ForeignTag_ReturnsFalse()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);
        var foreignTag = await MakeSvc(db.CreateContext()).CreateAsync(b, new TagInput("Foreign"));

        Guid linkId;
        await using (var ctx = db.CreateContext())
        {
            var link = EntityFactory.AnonymousLink(a);
            ctx.LinkEntities.Add(link);
            await ctx.SaveChangesAsync();
            linkId = link.Id;
        }

        var ok = await MakeSvc(db.CreateContext()).SetLinkTagsAsync(linkId, [foreignTag.Id], a);
        Assert.False(ok);
    }

    [Fact]
    public async Task SetLinkTagsAsync_ForeignLink_ReturnsFalse()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);

        Guid linkId;
        await using (var ctx = db.CreateContext())
        {
            var link = EntityFactory.AnonymousLink(a);
            ctx.LinkEntities.Add(link);
            await ctx.SaveChangesAsync();
            linkId = link.Id;
        }

        var ok = await MakeSvc(db.CreateContext()).SetLinkTagsAsync(linkId, [], b);
        Assert.False(ok);
    }
}
