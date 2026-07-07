using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Tests.Api;

public class MeLinksAnalyticsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeLinksAnalyticsTests(ApiFactory factory) => _factory = factory;

    private static readonly DateTimeOffset Day1 = new(2026, 6, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Day2 = new(2026, 6, 21, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Analytics_NewLink_HasZeroedBreakdowns()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        var body = await (await client.GetAsync($"/me/links/{link!.Id}/analytics"))
            .Content.ReadFromJsonAsync<LinkAnalyticsResponse>();

        Assert.Equal(0, body!.TotalClicks);
        Assert.Equal(0, body.UniqueClicks);
        Assert.Null(body.FirstClickAt);
        Assert.Empty(body.Sources);
        Assert.Empty(body.Devices);
        Assert.Empty(body.Timeline);
    }

    [Fact]
    public async Task Analytics_AnonymousLink_ReportsSourceDeviceUniqueAndTimeline()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        // Seed ten Twitter/Mobile clicks (clears the k=10 anonymity threshold; the first two share an
        // IP+hour so they dedupe) plus two below-threshold sources that must fold into "Other".
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            var shortCodeId = await db.ShortCodeEntities
                .Where(s => s.LinkId == link!.Id).Select(s => s.Id).FirstAsync();

            db.VisitEntities.AddRange(Enumerable.Range(0, 10).Select(i =>
                Visit(shortCodeId, $"ip{Math.Max(1, i)}", ClickSource.Twitter, DeviceType.Mobile, Day1)));
            db.VisitEntities.AddRange(
                Visit(shortCodeId, "ipA", ClickSource.Bluesky, DeviceType.Desktop, Day2),
                Visit(shortCodeId, "ipB", ClickSource.Direct, DeviceType.Desktop, Day2));
            await db.SaveChangesAsync();
        }

        var body = await (await client.GetAsync($"/me/links/{link!.Id}/analytics"))
            .Content.ReadFromJsonAsync<LinkAnalyticsResponse>();

        Assert.Equal(12, body!.TotalClicks);
        Assert.Equal(11, body.UniqueClicks); // ip1 appears twice (i=0 and i=1) ⇒ ip1–ip9 + ipA + ipB
        Assert.Equal(12, body.HumanClicks);  // no bot rows seeded
        Assert.Equal(0, body.BotClicks);

        // Twitter (10) survives the k=10 threshold; Bluesky (1) and Direct (1) fold into "Other".
        Assert.Equal(10, body.Sources.Single(s => s.Source == nameof(ClickSource.Twitter)).Count);
        Assert.Equal(2, body.Sources.Single(s => s.Source == "Other").Count);
        Assert.Equal(12, body.Sources.Sum(s => s.Count));
        Assert.DoesNotContain(body.Sources, s => s.Source == nameof(ClickSource.Bluesky));

        // Mobile (10) survives; Desktop (2) is below threshold and folds.
        Assert.Equal(10, body.Devices.Single(d => d.Device == nameof(DeviceType.Mobile)).Count);
        Assert.Equal(2, body.Devices.Single(d => d.Device == "Other").Count);

        Assert.Equal(2, body.Timeline.Count);
        Assert.Equal(10, body.Timeline.Single(t => t.Date == new DateOnly(2026, 6, 20)).Count);
        Assert.Equal(2, body.Timeline.Single(t => t.Date == new DateOnly(2026, 6, 21)).Count);
        Assert.Equal(Day1, body.FirstClickAt);
        Assert.Equal(Day2, body.LastClickAt);

        Assert.Equal(24, body.HourlyDistribution.Count);
        Assert.Equal(10, body.HourlyDistribution[10].Count); // Day1 clicks at 10:00 UTC
        Assert.Equal(2, body.HourlyDistribution[14].Count);  // Day2 clicks at 14:00 UTC
    }

    [Fact]
    public async Task AnalyticsExport_ReturnsAggregateCsv()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            var shortCodeId = await db.ShortCodeEntities
                .Where(s => s.LinkId == link!.Id).Select(s => s.Id).FirstAsync();
            db.VisitEntities.AddRange(Enumerable.Range(0, 10).Select(i =>
                Visit(shortCodeId, $"ip{i}", ClickSource.Twitter, DeviceType.Mobile, Day1)));
            await db.SaveChangesAsync();
        }

        var resp = await client.GetAsync($"/me/links/{link!.Id}/analytics/export");
        Assert.Equal("text/csv", resp.Content.Headers.ContentType!.MediaType);
        var csv = await resp.Content.ReadAsStringAsync();

        Assert.StartsWith("section,key,clicks,unique_clicks", csv);
        Assert.Contains("source,Twitter,10,", csv);
        // Aggregate-only: no hashed IPs, no per-click rows.
        Assert.DoesNotContain("ip0", csv);
    }

    private static VisitEntity Visit(Guid shortCodeId, string ip, ClickSource source, DeviceType device, DateTimeOffset at)
        => new()
        {
            Id = Guid.CreateVersion7(),
            ShortCodeId = shortCodeId,
            HashedIp = ip,
            Source = source,
            Device = device,
            ClickedAt = at,
        };
}
