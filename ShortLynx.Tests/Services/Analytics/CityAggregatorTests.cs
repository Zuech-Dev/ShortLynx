using ShortLynx.Services.Analytics;

namespace ShortLynx.Tests.Services.Analytics;

public class CityAggregatorTests
{
    [Fact]
    public void Summarize_CityMeetsThreshold_RevealsAtCityTier()
    {
        var rows = new[] { new CityDailyRow("Chicago", "IL", "US", Count: 20, UniqueCount: 6) };

        var result = CityAggregator.Summarize(rows);

        Assert.Equal(new CityCount("Chicago", "IL", "US", 20, CityGranularity.City), Assert.Single(result));
    }

    [Fact]
    public void Summarize_ManyClicksFromOneVisitor_RevealsOnlyAtCountryTier()
    {
        // The whole point of gating City/State on UniqueCount rather than Count: 50 clicks from a
        // single repeat visitor must not reveal a city or state on their own. The country tier is
        // unconditional though (see CityAggregator remarks) -- the same clicks still show up as "US",
        // just not attributed to a specific city or state.
        var rows = new[] { new CityDailyRow("Chicago", "IL", "US", Count: 50, UniqueCount: 1) };

        var result = CityAggregator.Summarize(rows);

        Assert.Equal(new CityCount(null, null, "US", 50, CityGranularity.Country), Assert.Single(result));
    }

    [Fact]
    public void Summarize_SumsAcrossMultipleDates_ForTheSameCity()
    {
        var rows = new[]
        {
            new CityDailyRow("Chicago", "IL", "US", Count: 10, UniqueCount: 4),
            new CityDailyRow("Chicago", "IL", "US", Count: 10, UniqueCount: 4), // a different day, same city
        };

        var result = CityAggregator.Summarize(rows);

        // 8 unique summed across two days clears k=6, even though neither single day would have.
        Assert.Equal(new CityCount("Chicago", "IL", "US", 20, CityGranularity.City), Assert.Single(result));
    }

    [Fact]
    public void Summarize_SameCityNameDifferentCountry_KeptSeparate()
    {
        var rows = new[]
        {
            new CityDailyRow("Paris", "IDF", "FR", Count: 12, UniqueCount: 8),
            new CityDailyRow("Paris", "TX", "US", Count: 12, UniqueCount: 8), // Paris, Texas
        };

        var result = CityAggregator.Summarize(rows);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.City == "Paris" && c.Country == "FR");
        Assert.Contains(result, c => c.City == "Paris" && c.Country == "US");
    }

    [Fact]
    public void Summarize_SameCityNameDifferentState_KeptSeparateAtCityTier()
    {
        // Both clear the city tier on their own. State must be part of the grouping key or these two
        // distinct places collapse into one inflated "Springfield, US" bucket -- they don't share a
        // state, so they must never be summed together.
        var rows = new[]
        {
            new CityDailyRow("Springfield", "IL", "US", Count: 10, UniqueCount: 7),
            new CityDailyRow("Springfield", "MO", "US", Count: 10, UniqueCount: 7),
        };

        var result = CityAggregator.Summarize(rows);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c is { City: "Springfield", State: "IL", Country: "US", Count: 10 });
        Assert.Contains(result, c => c is { City: "Springfield", State: "MO", Country: "US", Count: 10 });
    }

    [Fact]
    public void Summarize_CitiesBelowThreshold_FoldIntoState_WhenCombinedStateClears()
    {
        var rows = new[]
        {
            new CityDailyRow("Peoria", "IL", "US", Count: 10, UniqueCount: 3),     // 3 alone, short of 6
            new CityDailyRow("Naperville", "IL", "US", Count: 10, UniqueCount: 3), // 3 alone, short of 6
        };

        var result = CityAggregator.Summarize(rows);

        // Neither city clears k=6 alone, but IL combined does (3+3=6) -- revealed as a state, not a city.
        Assert.Equal(new CityCount(null, "IL", "US", 20, CityGranularity.State), Assert.Single(result));
    }

    [Fact]
    public void Summarize_StateBelowThreshold_RevealsCountryUnconditionally_EvenAtCountOne()
    {
        var rows = new[] { new CityDailyRow("Sheridan", "WY", "US", Count: 1, UniqueCount: 1) };

        var result = CityAggregator.Summarize(rows);

        // The country tier has no unique-visitor gate at all -- a single click from a single visitor
        // still reveals "US", matching the plain country breakdown already shown elsewhere in this app.
        Assert.Equal(new CityCount(null, null, "US", 1, CityGranularity.Country), Assert.Single(result));
    }

    [Fact]
    public void Summarize_NoCountryAtAll_FoldsIntoOther()
    {
        // Shouldn't happen after the BackgroundVisitWriter gate fix (a row now requires a resolved
        // Country to be written at all), but the aggregator must still degrade to Other rather than
        // mishandle a row that somehow has no country -- e.g. legacy data predating that fix.
        var rows = new[] { new CityDailyRow("Smalltown", null, null, Count: 5, UniqueCount: 1) };

        var result = CityAggregator.Summarize(rows);

        Assert.Equal(new CityCount(null, null, null, 5, CityGranularity.Other), Assert.Single(result));
    }

    [Fact]
    public void Summarize_TotalOutputCount_EqualsTotalInputCount_AcrossEveryTier()
    {
        var rows = new[]
        {
            new CityDailyRow("Chicago", "IL", "US", Count: 20, UniqueCount: 6),   // -> City tier
            new CityDailyRow("Peoria", "IL", "US", Count: 5, UniqueCount: 3),     // -> State tier (IL)
            new CityDailyRow("Naperville", "IL", "US", Count: 5, UniqueCount: 3), // -> State tier (IL)
            new CityDailyRow("Reno", "NV", "US", Count: 1, UniqueCount: 1),       // -> Country tier (US)
            new CityDailyRow(null, null, "CA", Count: 3, UniqueCount: 1),         // -> Country tier (CA)
            new CityDailyRow(null, null, null, Count: 2, UniqueCount: 1),         // -> Other
        };
        const long totalInput = 20 + 5 + 5 + 1 + 3 + 2;

        var result = CityAggregator.Summarize(rows);

        // No double-counting and no silent drops -- every input click lands in exactly one output bucket.
        Assert.Equal(totalInput, result.Sum(c => c.Count));
        Assert.Contains(result, c => c is { Granularity: CityGranularity.City, City: "Chicago", Count: 20 });
        Assert.Contains(result, c => c is { Granularity: CityGranularity.State, State: "IL", Count: 10 });
        Assert.Contains(result, c => c is { Granularity: CityGranularity.Country, Country: "US", Count: 1 });
        Assert.Contains(result, c => c is { Granularity: CityGranularity.Country, Country: "CA", Count: 3 });
        Assert.Contains(result, c => c is { Granularity: CityGranularity.Other, Count: 2 });
    }

    [Fact]
    public void Summarize_ZeroThreshold_DisablesFolding()
    {
        var rows = new[] { new CityDailyRow("Peoria", "IL", "US", Count: 3, UniqueCount: 1) };

        var result = CityAggregator.Summarize(rows, anonymityThreshold: 0);

        Assert.Equal(new CityCount("Peoria", "IL", "US", 3, CityGranularity.City), Assert.Single(result));
    }

    [Fact]
    public void Summarize_EmptyInput_YieldsEmptyResult()
        => Assert.Empty(CityAggregator.Summarize([]));
}
