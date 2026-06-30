using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Services.Campaigns;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Campaigns;

public class CampaignServiceTests
{
    private static CampaignService MakeSvc(ShortLynxDbContext ctx) => new(ctx);

    private static async Task<Guid> SeedAccountAsync(TestDatabase db)
    {
        var account = EntityFactory.Account();
        await using var ctx = db.CreateContext();
        ctx.AccountEntities.Add(account);
        await ctx.SaveChangesAsync();
        return account.Id;
    }

    [Fact]
    public async Task Create_StoresFields_AndNormalisesEmptyUtmToNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        var campaign = await MakeSvc(db.CreateContext())
            .CreateAsync(accountId, new CampaignInput("  Spring Launch  ", "  desc  ", "twitter", "  ", null));

        Assert.Equal("Spring Launch", campaign.Name);
        Assert.Equal("desc", campaign.Description);
        Assert.Equal("twitter", campaign.UtmSource);
        Assert.Null(campaign.UtmMedium);   // whitespace ⇒ null
        Assert.Null(campaign.UtmCampaign);
        Assert.Equal(accountId, campaign.AccountId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_EmptyName_Throws(string name)
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);

        await Assert.ThrowsAsync<ArgumentException>(
            () => MakeSvc(db.CreateContext()).CreateAsync(accountId, new CampaignInput(name)));
    }

    [Fact]
    public async Task List_IsAccountScoped()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);

        await MakeSvc(db.CreateContext()).CreateAsync(a, new CampaignInput("A1"));
        await MakeSvc(db.CreateContext()).CreateAsync(a, new CampaignInput("A2"));
        await MakeSvc(db.CreateContext()).CreateAsync(b, new CampaignInput("B1"));

        var listA = await MakeSvc(db.CreateContext()).ListAsync(a);
        Assert.Equal(2, listA.Count);
        Assert.All(listA, c => Assert.Equal(a, c.AccountId));
    }

    [Fact]
    public async Task Get_ForeignAccount_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);
        var created = await MakeSvc(db.CreateContext()).CreateAsync(a, new CampaignInput("A1"));

        Assert.NotNull(await MakeSvc(db.CreateContext()).GetAsync(created.Id, a));
        Assert.Null(await MakeSvc(db.CreateContext()).GetAsync(created.Id, b));
    }

    [Fact]
    public async Task Update_ReplacesFields()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var created = await MakeSvc(db.CreateContext())
            .CreateAsync(accountId, new CampaignInput("Old", "d", "src", "med", "camp"));

        var updated = await MakeSvc(db.CreateContext())
            .UpdateAsync(created.Id, accountId, new CampaignInput("New", null, "newsrc", null, null));

        Assert.NotNull(updated);
        Assert.Equal("New", updated!.Name);
        Assert.Null(updated.Description);   // cleared
        Assert.Equal("newsrc", updated.UtmSource);
        Assert.Null(updated.UtmMedium);     // cleared
    }

    [Fact]
    public async Task Update_ForeignAccount_ReturnsNull()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);
        var created = await MakeSvc(db.CreateContext()).CreateAsync(a, new CampaignInput("A1"));

        Assert.Null(await MakeSvc(db.CreateContext()).UpdateAsync(created.Id, b, new CampaignInput("hack")));
    }

    [Fact]
    public async Task Delete_RemovesCampaign_AndUnassignsItsLinks()
    {
        await using var db = await TestDatabase.CreateAsync();
        var accountId = await SeedAccountAsync(db);
        var campaign = await MakeSvc(db.CreateContext()).CreateAsync(accountId, new CampaignInput("Launch"));

        // Assign a link to the campaign.
        Guid linkId;
        await using (var ctx = db.CreateContext())
        {
            var link = EntityFactory.AnonymousLink(accountId);
            link.CampaignId = campaign.Id;
            ctx.LinkEntities.Add(link);
            await ctx.SaveChangesAsync();
            linkId = link.Id;
        }

        var deleted = await MakeSvc(db.CreateContext()).DeleteAsync(campaign.Id, accountId);

        Assert.True(deleted);
        await using var verify = db.CreateContext();
        Assert.False(await verify.CampaignEntities.AnyAsync(c => c.Id == campaign.Id));
        // The link survives, just unassigned.
        var survivor = await verify.LinkEntities.FirstAsync(l => l.Id == linkId);
        Assert.Null(survivor.CampaignId);
    }

    [Fact]
    public async Task Delete_ForeignAccount_ReturnsFalse()
    {
        await using var db = await TestDatabase.CreateAsync();
        var a = await SeedAccountAsync(db);
        var b = await SeedAccountAsync(db);
        var created = await MakeSvc(db.CreateContext()).CreateAsync(a, new CampaignInput("A1"));

        Assert.False(await MakeSvc(db.CreateContext()).DeleteAsync(created.Id, b));
    }
}
