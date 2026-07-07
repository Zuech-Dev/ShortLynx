using ShortLynx.Services.Analytics;

namespace ShortLynx.Tests.Services.Analytics;

public class RecipientEngagementTests
{
    private static readonly DateTimeOffset T0 = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Compute_Empty_YieldsZeroesAndNullPercentiles()
    {
        var s = RecipientEngagement.Compute([]);

        Assert.Equal(0, s.RecipientsTotal);
        Assert.Equal(0, s.RecipientsClicked);
        Assert.Null(s.MedianTimeToFirstClickMinutes);
        Assert.Null(s.P90TimeToFirstClickMinutes);
    }

    [Fact]
    public void Compute_CountsClickedVsUnclicked()
    {
        var s = RecipientEngagement.Compute(
        [
            (T0, T0.AddMinutes(5)),
            (T0, null),
            (T0, null),
        ]);

        Assert.Equal(3, s.RecipientsTotal);
        Assert.Equal(1, s.RecipientsClicked);
        Assert.Equal(5, s.MedianTimeToFirstClickMinutes);
    }

    [Fact]
    public void Compute_PercentilesUseNearestRank()
    {
        // Times to first click: 10, 20, 30, 40, 100 minutes.
        var s = RecipientEngagement.Compute(
        [
            (T0, T0.AddMinutes(10)),
            (T0, T0.AddMinutes(20)),
            (T0, T0.AddMinutes(30)),
            (T0, T0.AddMinutes(40)),
            (T0, T0.AddMinutes(100)),
        ]);

        Assert.Equal(5, s.RecipientsClicked);
        Assert.Equal(30, s.MedianTimeToFirstClickMinutes); // ceil(0.5·5) = 3rd of 5
        Assert.Equal(100, s.P90TimeToFirstClickMinutes);   // ceil(0.9·5) = 5th of 5
    }

    [Fact]
    public void Compute_ClampsClicksBeforeCreationToZero()
    {
        // Clock skew: first click recorded a minute before the code's CreatedAt.
        var s = RecipientEngagement.Compute([(T0, T0.AddMinutes(-1))]);

        Assert.Equal(1, s.RecipientsClicked);
        Assert.Equal(0, s.MedianTimeToFirstClickMinutes);
    }
}
