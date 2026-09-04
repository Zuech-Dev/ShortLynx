using ShortLynx.Services.Analytics;

namespace ShortLynx.Tests.Services.Analytics;

public class CityAggregatorTests
{
    [Fact]
    public void Summarize_CityBelowUniqueThreshold_FoldsIntoOther()
    {
        var rows = new[]
        {
            new CityDailyRow("Chicago", "US", Count: 20, UniqueCount: 6),  // meets k=6, revealed
            new CityDailyRow("Peoria", "US", Count: 20, UniqueCount: 5),   // one short, even with 20 clicks
        };

        var result = CityAggregator.Summarize(rows);

        Assert.Equal(2, result.Count);
        Assert.Equal(new CityCount("Chicago", "US", 20), result[0]);
        Assert.Equal(new CityCount("Other", null, 20), result[1]);
    }

    [Fact]
    public void Summarize_ManyClicksFromOneVisitor_StillFoldsIntoOther()
    {
        // The whole point of gating on UniqueCount rather than Count: 50 clicks from a single repeat
        // visitor must not reveal a city on their own.
        var rows = new[] { new CityDailyRow("Chicago", "US", Count: 50, UniqueCount: 1) };

        var result = CityAggregator.Summarize(rows);

        Assert.Equal(new CityCount("Other", null, 50), Assert.Single(result));
    }

    [Fact]
    public void Summarize_SumsAcrossMultipleDates_ForTheSameCity()
    {
        var rows = new[]
        {
            new CityDailyRow("Chicago", "US", Count: 10, UniqueCount: 4),
            new CityDailyRow("Chicago", "US", Count: 10, UniqueCount: 4), // a different day, same city
        };

        var result = CityAggregator.Summarize(rows);

        // 8 unique summed across two days clears k=6, even though neither single day would have.
        Assert.Equal(new CityCount("Chicago", "US", 20), Assert.Single(result));
    }

    [Fact]
    public void Summarize_SameCityNameDifferentCountry_KeptSeparate()
    {
        var rows = new[]
        {
            new CityDailyRow("Paris", "FR", Count: 12, UniqueCount: 8),
            new CityDailyRow("Paris", "US", Count: 12, UniqueCount: 8), // Paris, Texas
        };

        var result = CityAggregator.Summarize(rows);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.City == "Paris" && c.Country == "FR");
        Assert.Contains(result, c => c.City == "Paris" && c.Country == "US");
    }

    [Fact]
    public void Summarize_ZeroThreshold_DisablesFolding()
    {
        var rows = new[] { new CityDailyRow("Peoria", "US", Count: 3, UniqueCount: 1) };

        var result = CityAggregator.Summarize(rows, anonymityThreshold: 0);

        Assert.Equal(new CityCount("Peoria", "US", 3), Assert.Single(result));
    }

    [Fact]
    public void Summarize_EmptyInput_YieldsEmptyResult()
        => Assert.Empty(CityAggregator.Summarize([]));
}
