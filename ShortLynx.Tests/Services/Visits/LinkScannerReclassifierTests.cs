using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Visits;

namespace ShortLynx.Tests.Services.Visits;

public class LinkScannerReclassifierTests
{
    private static readonly DateTimeOffset Cutoff = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Before = Cutoff.AddDays(-1);
    private static readonly DateTimeOffset After = Cutoff.AddHours(1);

    private static VisitEntity Visit(DeviceType device, string? nav, DateTimeOffset at) => new()
    {
        Id = Guid.CreateVersion7(),
        HashedIp = "ip",
        Device = device,
        NavigationType = nav,
        ClickedAt = at,
    };

    private static async Task<(Dictionary<string, Guid> Ids, Guid UserVisitScanner, Guid UserVisitHuman)> SeedAsync(TestDatabase db)
    {
        var rows = new Dictionary<string, VisitEntity>
        {
            ["desktopScanner"] = Visit(DeviceType.Desktop, null, Before),
            ["mobileScanner"] = Visit(DeviceType.Mobile, null, Before),
            ["tabletScanner"] = Visit(DeviceType.Tablet, null, Before),
            ["human"] = Visit(DeviceType.Desktop, "cross-site", Before),
            // Privacy-signal clicks are stored Unknown with no NavigationType: people, never scanners.
            ["privacy"] = Visit(DeviceType.Unknown, null, Before),
            ["bot"] = Visit(DeviceType.Bot, null, Before),
            ["afterCutoff"] = Visit(DeviceType.Desktop, null, After),
        };

        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        var ulc = EntityFactory.UserLinkCode(link.Id, Guid.CreateVersion7(), "rc000001");
        var uvScanner = EntityFactory.UserVisit(ulc.Id, ulc.UserId);
        uvScanner.Device = DeviceType.Mobile;
        uvScanner.ClickedAt = Before;
        var uvHuman = EntityFactory.UserVisit(ulc.Id, ulc.UserId);
        uvHuman.Device = DeviceType.Mobile;
        uvHuman.NavigationType = "none";
        uvHuman.ClickedAt = Before;

        await using var ctx = db.CreateContext();
        ctx.AddRange(account, link, ulc, uvScanner, uvHuman);
        ctx.VisitEntities.AddRange(rows.Values);
        await ctx.SaveChangesAsync();
        return (rows.ToDictionary(r => r.Key, r => r.Value.Id), uvScanner.Id, uvHuman.Id);
    }

    [Fact]
    public async Task Run_ReclassifiesOnlyBrowserRowsWithoutSecFetchSite_BeforeCutoff()
    {
        await using var db = await TestDatabase.CreateAsync();
        var (ids, uvScanner, uvHuman) = await SeedAsync(db);

        var result = await LinkScannerReclassifier.RunAsync(db.CreateContext(), Cutoff, dryRun: false);

        Assert.Equal(new ScannerReclassifyResult(3, 1, false), result);
        await using var ctx = db.CreateContext();
        var device = await ctx.VisitEntities.ToDictionaryAsync(v => v.Id, v => v.Device);
        Assert.Equal(DeviceType.SuspectedAutomated, device[ids["desktopScanner"]]);
        Assert.Equal(DeviceType.SuspectedAutomated, device[ids["mobileScanner"]]);
        Assert.Equal(DeviceType.SuspectedAutomated, device[ids["tabletScanner"]]);
        Assert.Equal(DeviceType.Desktop, device[ids["human"]]);
        Assert.Equal(DeviceType.Unknown, device[ids["privacy"]]);
        Assert.Equal(DeviceType.Bot, device[ids["bot"]]);
        Assert.Equal(DeviceType.Desktop, device[ids["afterCutoff"]]);

        var userDevice = await ctx.UserVisitEntities.ToDictionaryAsync(v => v.Id, v => v.Device);
        Assert.Equal(DeviceType.SuspectedAutomated, userDevice[uvScanner]);
        Assert.Equal(DeviceType.Mobile, userDevice[uvHuman]);
    }

    [Fact]
    public async Task DryRun_CountsWithoutChangingAnything()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedAsync(db);

        var result = await LinkScannerReclassifier.RunAsync(db.CreateContext(), Cutoff, dryRun: true);

        Assert.Equal(new ScannerReclassifyResult(3, 1, true), result);
        await using var ctx = db.CreateContext();
        Assert.False(await ctx.VisitEntities.AnyAsync(v => v.Device == DeviceType.SuspectedAutomated));
        Assert.False(await ctx.UserVisitEntities.AnyAsync(v => v.Device == DeviceType.SuspectedAutomated));
    }

    [Fact]
    public async Task Run_IsIdempotent()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedAsync(db);

        await LinkScannerReclassifier.RunAsync(db.CreateContext(), Cutoff, dryRun: false);
        var second = await LinkScannerReclassifier.RunAsync(db.CreateContext(), Cutoff, dryRun: false);

        Assert.Equal(new ScannerReclassifyResult(0, 0, false), second);
    }
}
