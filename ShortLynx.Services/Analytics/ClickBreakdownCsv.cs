using System.Text;

namespace ShortLynx.Services.Analytics;

/// <summary>
/// Renders a <see cref="ClickBreakdown"/> as CSV — and only a breakdown, never visit rows. Exports are
/// aggregate-only by decision (MASTER_PLAN P2): a row-per-click file for a small campaign is a
/// deanonymization list, so the export mirrors exactly what the dashboard shows. The k=10 fold has
/// already been applied by <see cref="ClickAggregator.Summarize"/> before data reaches this formatter.
/// </summary>
public static class ClickBreakdownCsv
{
    public static string Format(ClickBreakdown b)
    {
        var sb = new StringBuilder();
        sb.AppendLine("section,key,clicks,unique_clicks");

        Row(sb, "totals", "total", b.TotalClicks, b.UniqueClicks);
        Row(sb, "totals", "human", b.HumanClicks, b.HumanUniqueClicks);
        Row(sb, "totals", "bot", b.BotClicks, null);

        foreach (var s in b.Sources) Row(sb, "source", s.Source, s.Count, null);
        foreach (var d in b.Devices) Row(sb, "device", d.Device, d.Count, null);
        Section(sb, "browser", b.Browsers);
        Section(sb, "os", b.OperatingSystems);
        Section(sb, "language", b.Languages);
        Section(sb, "country", b.Countries);
        Section(sb, "navigation", b.NavigationTypes);
        Section(sb, "utm_source", b.UtmSources);
        Section(sb, "utm_medium", b.UtmMediums);
        Section(sb, "utm_campaign", b.UtmCampaigns);

        foreach (var t in b.Timeline)
            Row(sb, "day", t.Date.ToString("yyyy-MM-dd"), t.Count, t.UniqueCount);
        foreach (var h in b.HourlyDistribution)
            Row(sb, "hour_utc", h.Hour.ToString("00"), h.Count, null);
        if (b.LocalHourlyDistribution.Any(h => h.Count > 0))
            foreach (var h in b.LocalHourlyDistribution)
                Row(sb, "hour_local", h.Hour.ToString("00"), h.Count, null);

        return sb.ToString();
    }

    private static void Section(StringBuilder sb, string name, IReadOnlyList<LabelCount> items)
    {
        foreach (var i in items) Row(sb, name, i.Label, i.Count, null);
    }

    private static void Row(StringBuilder sb, string section, string key, long clicks, long? unique)
        => sb.Append(section).Append(',').Append(Escape(key)).Append(',').Append(clicks)
            .Append(',').Append(unique?.ToString() ?? "").AppendLine();

    // Labels are low-entropy derived values, but UTM keys are operator-supplied text — quote anything
    // that could break the row shape.
    private static string Escape(string value)
        => value.Contains(',') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
}
