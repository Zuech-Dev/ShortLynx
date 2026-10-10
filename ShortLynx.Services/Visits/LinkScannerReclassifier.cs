using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Enums;

namespace ShortLynx.Services.Visits;

/// <summary>Rows that matched (dry run) or were changed, per visits table.</summary>
public sealed record ScannerReclassifyResult(int Visits, int UserVisits, bool DryRun);

/// <summary>
/// One-off backfill of <see cref="DeviceType.SuspectedAutomated"/> onto clicks stored before the writer
/// learned to classify link scanners. The stored rows already carry everything the write-time rule uses:
/// <list type="bullet">
/// <item>a browser device class (Desktop/Mobile/Tablet) — and privacy-signal clicks are always stored as
/// <see cref="DeviceType.Unknown"/> with no dimensions, so a browser-class row never carried DNT/GPC;</item>
/// <item><c>NavigationType</c>, the reduced <c>Sec-Fetch-Site</c> value, null when the header was absent.</item>
/// </list>
/// What rows don't record is whether the request was HTTPS, which the rule also needs (browsers omit
/// <c>Sec-Fetch-*</c> to insecure origins). Only an operator knows that about their own deployment, so this
/// never runs automatically: it's invoked explicitly with a cutoff (<c>POST /admin/maintenance/...</c>),
/// covering only the period the operator knows redirects were served over HTTPS.
/// </summary>
public static class LinkScannerReclassifier
{
    public static async Task<ScannerReclassifyResult> RunAsync(
        ShortLynxDbContext db, DateTimeOffset before, bool dryRun, CancellationToken ct = default)
    {
        // SQLite can't compare DateTimeOffset in SQL (see VisitRetentionService.PruneOnceAsync): resolve
        // the matching ids client-side there. PostgreSQL takes the set-based path.
        if (db.Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true)
        {
            var visitIds = (await db.VisitEntities
                    .Where(v => (v.Device == DeviceType.Desktop || v.Device == DeviceType.Mobile || v.Device == DeviceType.Tablet)
                                && v.NavigationType == null)
                    .Select(v => new { v.Id, v.ClickedAt }).ToListAsync(ct))
                .Where(v => v.ClickedAt < before).Select(v => v.Id).ToList();
            var userVisitIds = (await db.UserVisitEntities
                    .Where(v => (v.Device == DeviceType.Desktop || v.Device == DeviceType.Mobile || v.Device == DeviceType.Tablet)
                                && v.NavigationType == null)
                    .Select(v => new { v.Id, v.ClickedAt }).ToListAsync(ct))
                .Where(v => v.ClickedAt < before).Select(v => v.Id).ToList();
            if (dryRun) return new(visitIds.Count, userVisitIds.Count, true);

            var visits = 0;
            foreach (var chunk in visitIds.Chunk(500))
                visits += await db.VisitEntities.Where(v => chunk.Contains(v.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(v => v.Device, DeviceType.SuspectedAutomated), ct);
            var userVisits = 0;
            foreach (var chunk in userVisitIds.Chunk(500))
                userVisits += await db.UserVisitEntities.Where(v => chunk.Contains(v.Id))
                    .ExecuteUpdateAsync(s => s.SetProperty(v => v.Device, DeviceType.SuspectedAutomated), ct);
            return new(visits, userVisits, false);
        }

        var visitRows = db.VisitEntities.Where(v =>
            (v.Device == DeviceType.Desktop || v.Device == DeviceType.Mobile || v.Device == DeviceType.Tablet)
            && v.NavigationType == null && v.ClickedAt < before);
        var userVisitRows = db.UserVisitEntities.Where(v =>
            (v.Device == DeviceType.Desktop || v.Device == DeviceType.Mobile || v.Device == DeviceType.Tablet)
            && v.NavigationType == null && v.ClickedAt < before);

        if (dryRun)
            return new(await visitRows.CountAsync(ct), await userVisitRows.CountAsync(ct), true);

        return new(
            await visitRows.ExecuteUpdateAsync(s => s.SetProperty(v => v.Device, DeviceType.SuspectedAutomated), ct),
            await userVisitRows.ExecuteUpdateAsync(s => s.SetProperty(v => v.Device, DeviceType.SuspectedAutomated), ct),
            false);
    }
}
