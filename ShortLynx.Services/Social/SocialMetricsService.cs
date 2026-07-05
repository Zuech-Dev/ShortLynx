using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Social;

public sealed class SocialMetricsService(
    ShortLynxDbContext db,
    IEnumerable<ISocialConnector> connectors,
    ITokenProtector protector,
    IOptions<SocialMetricsOptions> options) : ISocialMetricsService
{
    public async Task<int> RefreshLinkAsync(Guid accountId, Guid linkId, CancellationToken ct = default)
    {
        var posts = await db.SocialPostEntities
            .Include(p => p.SocialConnection)
            .Where(p => p.LinkId == linkId && p.AccountId == accountId && p.SocialConnectionId != null)
            .ToListAsync(ct);

        return await RefreshPostsAsync(posts, ct);
    }

    public async Task<int> RefreshRecentAsync(CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var windowStart = now.AddDays(-Math.Max(1, options.Value.RefreshWindowDays));
        var staleBefore = now.AddMinutes(-Math.Max(1, options.Value.RefreshIntervalMinutes));

        // Two-phase: the time comparisons must run in memory (SQLite — used by the test suite — can't
        // filter DateTimeOffset server-side), but phase 1 projects only (id, dates) instead of dragging
        // every post's text + joined connection into memory, so the per-pass cost of the full scan stays
        // small as the table grows. Phase 2 then materializes just the due posts. Disconnected posts are
        // skipped — no tokens to pull with.
        var candidates = await db.SocialPostEntities
            .Where(p => p.SocialConnectionId != null)
            .Select(p => new { p.Id, p.PostedAt, p.MetricsUpdatedAt })
            .ToListAsync(ct);

        var dueIds = candidates
            .Where(p => p.PostedAt >= windowStart)
            .Where(p => p.MetricsUpdatedAt == null || p.MetricsUpdatedAt < staleBefore)
            .Select(p => p.Id)
            .ToList();
        if (dueIds.Count == 0) return 0;

        var due = await db.SocialPostEntities
            .Include(p => p.SocialConnection)
            .Where(p => dueIds.Contains(p.Id))
            .ToListAsync(ct);

        return await RefreshPostsAsync(due, ct);
    }

    private async Task<int> RefreshPostsAsync(List<SocialPostEntity> posts, CancellationToken ct)
    {
        var updated = 0;
        foreach (var post in posts)
        {
            var connection = post.SocialConnection;
            if (connection is null) continue;

            var connector = connectors.FirstOrDefault(c => c.Platform == post.Platform);
            if (connector is null) continue;

            try
            {
                var metrics = await ConnectorTokenGuard.ExecuteAsync(
                    db, protector, connector, connection,
                    context => connector.GetPostMetricsAsync(context, post.ExternalPostId, ct), ct);

                if (metrics is not null)
                {
                    post.Impressions = metrics.Impressions;
                    post.Likes = metrics.Likes;
                    post.Reposts = metrics.Reposts;
                    post.Replies = metrics.Replies;
                }
                // Deleted-on-platform posts (null) keep their last-known counts; the timestamp still
                // advances so the scheduler doesn't hammer a dead post every pass.
                post.MetricsUpdatedAt = DateTimeOffset.UtcNow;
                updated++;
            }
            catch (TokenExpiredException) { /* needs reconnect — surfaced on the next manual action */ }
            catch (ArgumentException) { /* platform rejected the request for this post — skip it */ }
            catch (HttpRequestException) { /* platform unreachable — try again next pass */ }
        }

        if (updated > 0) await db.SaveChangesAsync(ct);
        return updated;
    }
}
