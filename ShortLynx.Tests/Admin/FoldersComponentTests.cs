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
using ShortLynx.Services.Folders;

namespace ShortLynx.Tests.Admin;

public class FoldersComponentTests : BunitContext
{
    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();
    private readonly Guid _accountId;

    public FoldersComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ShortLynxDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>().CreateDbContext());
        Services.AddScoped<IFolderService, FolderService>();

        var auth = AddAuthorization();
        auth.SetAuthorized("user@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, _uid.ToString()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        _accountId = AccountTestSeed.SeedOwner(db, _uid);
    }

    private Guid SeedFolder(string name = "Docs")
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        var f = new FolderEntity
        {
            Id = Guid.CreateVersion7(), AccountId = _accountId, Name = name, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.FolderEntities.Add(f);
        db.SaveChanges();
        return f.Id;
    }

    [Fact]
    public void Folders_EmptyState_RendersWhenNoneExist()
    {
        var cut = Render<Folders>();
        cut.WaitForElement("[data-testid=empty]");
        Assert.NotNull(cut.Find("[data-testid=empty]"));
    }

    [Fact]
    public void Folders_Create_AddsFolderToList()
    {
        var cut = Render<Folders>();

        cut.Find("[data-testid=new-folder]").Click();
        cut.Find("[data-testid=name-input]").Change("Marketing");
        cut.Find("[data-testid=add-submit]").Click();

        var row = cut.WaitForElement("[data-testid=folder-row]");
        Assert.Contains("Marketing", row.InnerHtml);

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        Assert.True(db.FolderEntities.Any(f => f.Name == "Marketing" && f.AccountId == _accountId));
    }

    [Fact]
    public void Folders_ListsExistingWithLinkCount()
    {
        var folderId = SeedFolder("Existing");
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            db.LinkEntities.Add(new LinkEntity
            {
                Id = Guid.CreateVersion7(), OriginalUrl = "https://example.com", Mode = LinkMode.Anonymous,
                AccountId = _accountId, FolderId = folderId, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            });
            db.SaveChanges();
        }

        var cut = Render<Folders>();
        var count = cut.WaitForElement("[data-testid=link-count]");
        Assert.Contains("1 link", count.TextContent);
    }
}

public class FolderDetailComponentTests : BunitContext
{
    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();
    private readonly Guid _accountId;

    public FolderDetailComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<ShortLynxDbContext>(sp =>
            sp.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>().CreateDbContext());
        Services.AddScoped<IFolderService, FolderService>();

        var auth = AddAuthorization();
        auth.SetAuthorized("user@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, _uid.ToString()));
        JSInterop.Mode = JSRuntimeMode.Loose;

        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        _accountId = AccountTestSeed.SeedOwner(db, _uid);
    }

    [Fact]
    public void FolderDetail_RendersNameAndLinkTable()
    {
        Guid folderId;
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            var folder = new FolderEntity
            {
                Id = Guid.CreateVersion7(), AccountId = _accountId, Name = "Docs", CreatedAt = DateTimeOffset.UtcNow,
            };
            var link = new LinkEntity
            {
                Id = Guid.CreateVersion7(), OriginalUrl = "https://example.com", Mode = LinkMode.Anonymous,
                AccountId = _accountId, FolderId = folder.Id, Nickname = "Homepage",
                CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            };
            db.AddRange(folder, link);
            db.SaveChanges();
            folderId = folder.Id;
        }

        var cut = Render<FolderDetail>(p => p.Add(c => c.Id, folderId));

        cut.WaitForElement("[data-testid=link-table]");
        Assert.Contains("Docs", cut.Markup);
        Assert.Contains("Homepage", cut.Find("[data-testid=link-table]").InnerHtml);
    }

    [Fact]
    public void FolderDetail_ForeignFolder_ShowsNotFound()
    {
        Guid otherId;
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using (var db = factory.CreateDbContext())
        {
            var otherAccount = new AccountEntity
            {
                Id = Guid.CreateVersion7(), Name = "Other Co", CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            };
            var f = new FolderEntity
            {
                Id = Guid.CreateVersion7(), AccountId = otherAccount.Id, Name = "Theirs",
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.AddRange(otherAccount, f);
            db.SaveChanges();
            otherId = f.Id;
        }

        var cut = Render<FolderDetail>(p => p.Add(c => c.Id, otherId));
        cut.WaitForState(() => cut.Markup.Contains("not found", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("not found", cut.Markup, StringComparison.OrdinalIgnoreCase);
    }
}
