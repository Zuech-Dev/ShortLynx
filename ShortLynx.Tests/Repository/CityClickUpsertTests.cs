using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Operations;
using ShortLynx.Repository;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Repository;

// Exercises CityClickUpsert.RunAsync's actual dedup/grouping SQL against a real DbContext, via the
// public EfCoreDbOperations entry point -- BackgroundVisitWriterTests only goes through a fake sink
// that bypasses this logic entirely.
public class CityClickUpsertTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static async Task<Guid> SeedLinkAsync(TestDatabase testDb)
    {
        await using var db = testDb.CreateContext();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        db.AddRange(account, link);
        await db.SaveChangesAsync();
        return link.Id;
    }

    [Fact]
    public async Task UpsertCityClicksAsync_FreshInsert_CreatesDailyAndVisitorRows()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var linkId = await SeedLinkAsync(testDb);

        await using (var db = testDb.CreateContext())
        {
            await new EfCoreDbOperations(db).UpsertCityClicksAsync(
                [new CityClickItem(LinkId: linkId, City: "Chicago", State: "IL", Country: "US", Date: Today, HashedIp: "h1")]);
        }

        await using var check = testDb.CreateContext();
        var daily = Assert.Single(check.CityClickDailyEntities);
        Assert.Equal(("Chicago", "IL", "US", 1L, 1L), (daily.City, daily.State, daily.Country, daily.Count, daily.UniqueCount));
        var visitor = Assert.Single(check.CityClickDailyVisitorEntities);
        Assert.Equal(("Chicago", "IL", "US", "h1"), (visitor.City, visitor.State, visitor.Country, visitor.HashedIp));
    }

    [Fact]
    public async Task UpsertCityClicksAsync_SameVisitorClicksAgainSameDay_CountIncrements_UniqueCountDoesNot()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var linkId = await SeedLinkAsync(testDb);
        var item = new CityClickItem(LinkId: linkId, City: "Chicago", State: "IL", Country: "US", Date: Today, HashedIp: "h1");

        // Two separate flushes, not one batch -- exercises the cross-flush "already recorded today"
        // lookup (CityClickUpsert's `existing` query), not just in-batch Distinct().
        await using (var db = testDb.CreateContext())
            await new EfCoreDbOperations(db).UpsertCityClicksAsync([item]);
        await using (var db = testDb.CreateContext())
            await new EfCoreDbOperations(db).UpsertCityClicksAsync([item]);

        await using var check = testDb.CreateContext();
        var daily = Assert.Single(check.CityClickDailyEntities);
        Assert.Equal(2, daily.Count);
        Assert.Equal(1, daily.UniqueCount); // same hashed IP both times -- not a second distinct visitor
        Assert.Single(check.CityClickDailyVisitorEntities); // no duplicate presence marker either
    }

    [Fact]
    public async Task UpsertCityClicksAsync_DifferentVisitorsSameCityAndDay_IncrementsUniqueCount()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var linkId = await SeedLinkAsync(testDb);

        await using (var db = testDb.CreateContext())
        {
            await new EfCoreDbOperations(db).UpsertCityClicksAsync(
            [
                new CityClickItem(LinkId: linkId, City: "Chicago", State: "IL", Country: "US", Date: Today, HashedIp: "h1"),
                new CityClickItem(LinkId: linkId, City: "Chicago", State: "IL", Country: "US", Date: Today, HashedIp: "h2"),
            ]);
        }

        await using var check = testDb.CreateContext();
        var daily = Assert.Single(check.CityClickDailyEntities);
        Assert.Equal(2, daily.Count);
        Assert.Equal(2, daily.UniqueCount);
    }

    [Fact]
    public async Task UpsertCityClicksAsync_SameCityNameDifferentState_KeptAsSeparateRows()
    {
        await using var testDb = await TestDatabase.CreateAsync();
        var linkId = await SeedLinkAsync(testDb);

        await using (var db = testDb.CreateContext())
        {
            await new EfCoreDbOperations(db).UpsertCityClicksAsync(
            [
                new CityClickItem(LinkId: linkId, City: "Springfield", State: "IL", Country: "US", Date: Today, HashedIp: "h1"),
                new CityClickItem(LinkId: linkId, City: "Springfield", State: "MO", Country: "US", Date: Today, HashedIp: "h2"),
            ]);
        }

        await using var check = testDb.CreateContext();
        Assert.Equal(2, check.CityClickDailyEntities.Count());
        Assert.Contains(check.CityClickDailyEntities, d => d.City == "Springfield" && d.State == "IL" && d.Count == 1);
        Assert.Contains(check.CityClickDailyEntities, d => d.City == "Springfield" && d.State == "MO" && d.Count == 1);
    }

    [Fact]
    public async Task UpsertCityClicksAsync_CityNull_StateAndCountryPopulated_StillWritesARow()
    {
        // The companion case to the BackgroundVisitWriter gate fix (d.Country is not null, not
        // d.City is not null): a row can resolve Country/State without a specific City, and the
        // persistence layer must accept and store that rather than require City.
        await using var testDb = await TestDatabase.CreateAsync();
        var linkId = await SeedLinkAsync(testDb);

        await using (var db = testDb.CreateContext())
        {
            await new EfCoreDbOperations(db).UpsertCityClicksAsync(
                [new CityClickItem(LinkId: linkId, City: null, State: "CA", Country: "US", Date: Today, HashedIp: "h1")]);
        }

        await using var check = testDb.CreateContext();
        var daily = Assert.Single(check.CityClickDailyEntities);
        Assert.Null(daily.City);
        Assert.Equal("CA", daily.State);
        Assert.Equal("US", daily.Country);
        Assert.Equal(1, daily.Count);
        Assert.Equal(1, daily.UniqueCount);
    }
}
