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
            (T0, T0.AddMinutes(5), 1L),
            (T0, null, 0L),
            (T0, null, 0L),
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
            (T0, T0.AddMinutes(10), 1L),
            (T0, T0.AddMinutes(20), 1L),
            (T0, T0.AddMinutes(30), 1L),
            (T0, T0.AddMinutes(40), 1L),
            (T0, T0.AddMinutes(100), 1L),
        ]);

        Assert.Equal(5, s.RecipientsClicked);
        Assert.Equal(30, s.MedianTimeToFirstClickMinutes); // ceil(0.5·5) = 3rd of 5
        Assert.Equal(100, s.P90TimeToFirstClickMinutes);   // ceil(0.9·5) = 5th of 5
    }

    [Fact]
    public void Compute_RepeatEngagement_CountsReturningRecipients()
    {
        // Mode 2 repeat clicks are EXACT and unbounded in time — a recipient code identifies the
        // person, so no IP hash (and therefore no hourly dedup window) is involved. A recipient
        // returning to the link repeatedly is the engagement signal this measures.
        var s = RecipientEngagement.Compute(
        [
            (T0, T0.AddMinutes(5), 4L),   // came back three more times
            (T0, T0.AddMinutes(9), 2L),   // came back once
            (T0, T0.AddMinutes(30), 1L),  // clicked once
            (T0, null, 0L),               // never clicked
        ]);

        Assert.Equal(4, s.RecipientsTotal);
        Assert.Equal(3, s.RecipientsClicked);
        Assert.Equal(2, s.RecipientsClickedMoreThanOnce);
        Assert.Equal(4, s.MaxClicksBySingleRecipient);
        // Averaged over recipients who clicked at all (7 clicks / 3 engaged), not all provisioned.
        Assert.Equal(2.33, s.AverageClicksPerEngagedRecipient);
    }

    [Fact]
    public void Compute_NoClicks_YieldsZeroRepeatStats()
    {
        var s = RecipientEngagement.Compute([(T0, null, 0L), (T0, null, 0L)]);

        Assert.Equal(0, s.RecipientsClickedMoreThanOnce);
        Assert.Equal(0, s.AverageClicksPerEngagedRecipient); // no divide-by-zero
        Assert.Equal(0, s.MaxClicksBySingleRecipient);
    }

    [Fact]
    public void Compute_ClampsClicksBeforeCreationToZero()
    {
        // Clock skew: first click recorded a minute before the code's CreatedAt.
        var s = RecipientEngagement.Compute([(T0, T0.AddMinutes(-1), 1L)]);

        Assert.Equal(1, s.RecipientsClicked);
        Assert.Equal(0, s.MedianTimeToFirstClickMinutes);
    }
}
