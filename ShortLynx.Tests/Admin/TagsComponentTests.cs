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
using ShortLynx.Services.Tags;

namespace ShortLynx.Tests.Admin;

public class TagsComponentTests : BunitContext
{
    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();
    private readonly Guid _accountId;

    public TagsComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ShortLynxDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>().CreateDbContext());
        Services.AddScoped<ITagService, TagService>();

        var auth = AddAuthorization();
        auth.SetAuthorized("user@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, _uid.ToString()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        _accountId = AccountTestSeed.SeedOwner(db, _uid);
    }

    private Guid SeedTag(string name = "Sale")
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        var t = new TagEntity
        {
            Id = Guid.CreateVersion7(), AccountId = _accountId, Name = name, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.TagEntities.Add(t);
        db.SaveChanges();
        return t.Id;
    }

    [Fact]
    public void Tags_EmptyState_RendersWhenNoneExist()
    {
        var cut = Render<Tags>();
        cut.WaitForElement("[data-testid=empty]");
        Assert.NotNull(cut.Find("[data-testid=empty]"));
    }

    [Fact]
    public void Tags_Create_AddsTagToList()
    {
        var cut = Render<Tags>();

        cut.Find("[data-testid=new-tag]").Click();
        cut.Find("[data-testid=name-input]").Change("Spring");
        cut.Find("[data-testid=add-submit]").Click();

        var row = cut.WaitForElement("[data-testid=tag-row]");
        Assert.Contains("Spring", row.InnerHtml);

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        Assert.True(db.TagEntities.Any(t => t.Name == "Spring" && t.AccountId == _accountId));
    }

    [Fact]
    public void Tags_Create_DuplicateName_ShowsError()
    {
        SeedTag("Sale");
        var cut = Render<Tags>();

        cut.Find("[data-testid=new-tag]").Click();
        cut.Find("[data-testid=name-input]").Change("Sale");
        cut.Find("[data-testid=add-submit]").Click();

        var error = cut.WaitForElement("[data-testid=add-error]");
        Assert.NotEmpty(error.TextContent);
    }

    [Fact]
    public void Tags_ListsExistingWithLinkCount()
    {
        var tagId = SeedTag("Existing");
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            var link = new LinkEntity
            {
                Id = Guid.CreateVersion7(), OriginalUrl = "https://example.com", Mode = LinkMode.Anonymous,
                AccountId = _accountId, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            };
            db.LinkEntities.Add(link);
            db.LinkTagEntities.Add(new LinkTagEntity
            {
                Id = Guid.CreateVersion7(), LinkId = link.Id, TagId = tagId, CreatedAt = DateTimeOffset.UtcNow,
            });
            db.SaveChanges();
        }

        var cut = Render<Tags>();
        var row = cut.WaitForElement("[data-testid=tag-row]");
        Assert.Contains("1", row.TextContent);
    }

    [Fact]
    public void Tags_Remove_DeletesTag()
    {
        var tagId = SeedTag("Removable");
        var cut = Render<Tags>();
        cut.WaitForElement("[data-testid=remove-btn]").Click();

        cut.WaitForState(() => cut.Markup.Contains("No tags yet", StringComparison.OrdinalIgnoreCase));

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        Assert.False(db.TagEntities.Any(t => t.Id == tagId));
    }
}
