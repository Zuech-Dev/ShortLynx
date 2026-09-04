namespace ShortLynx.Services.Analytics;

/// <summary>
/// Analytics-wide toggles, bound from config section "Analytics".
/// </summary>
public class AnalyticsOptions
{
    /// <summary>
    /// True (the default) applies k-anonymity suppression to every breakdown exactly as documented on
    /// <see cref="ClickAggregator.AnonymityThreshold"/>. The false path exists ONLY for local
    /// development: with the low, non-representative traffic volumes a dev environment produces, real
    /// k-anonymity folds almost everything into "Other", which makes the breakdown useless for testing
    /// against. Setting this false surfaces every dimension unsuppressed. Never set false in any
    /// deployment serving real visitor traffic -- it defeats the privacy property the threshold exists
    /// for.
    /// </summary>
    public bool EnforceAnonymity { get; set; } = true;
}
