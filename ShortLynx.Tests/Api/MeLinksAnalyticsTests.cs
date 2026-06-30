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

        // Seed visits directly: two from one device/IP (dedup), then two more on a second day.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            var shortCodeId = await db.ShortCodeEntities
                .Where(s => s.LinkId == link!.Id).Select(s => s.Id).FirstAsync();

            db.VisitEntities.AddRange(
                Visit(shortCodeId, "ip1", ClickSource.Twitter, DeviceType.Mobile, Day1),
                Visit(shortCodeId, "ip1", ClickSource.Twitter, DeviceType.Mobile, Day1), // same IP+hour ⇒ not unique
                Visit(shortCodeId, "ip2", ClickSource.Bluesky, DeviceType.Desktop, Day2),
                Visit(shortCodeId, "ip3", ClickSource.Direct, DeviceType.Desktop, Day2));
            await db.SaveChangesAsync();
        }

        var body = await (await client.GetAsync($"/me/links/{link!.Id}/analytics"))
            .Content.ReadFromJsonAsync<LinkAnalyticsResponse>();

        Assert.Equal(4, body!.TotalClicks);
        Assert.Equal(3, body.UniqueClicks); // ip1, ip2, ip3

        Assert.Equal(2, body.Sources.Single(s => s.Source == nameof(ClickSource.Twitter)).Count);
        Assert.Equal(1, body.Sources.Single(s => s.Source == nameof(ClickSource.Bluesky)).Count);
        Assert.Equal(4, body.Sources.Sum(s => s.Count));

        Assert.Equal(2, body.Devices.Single(d => d.Device == nameof(DeviceType.Desktop)).Count);
        Assert.Equal(2, body.Devices.Single(d => d.Device == nameof(DeviceType.Mobile)).Count);

        Assert.Equal(2, body.Timeline.Count);
        Assert.Equal(2, body.Timeline.Single(t => t.Date == new DateOnly(2026, 6, 20)).Count);
        Assert.Equal(2, body.Timeline.Single(t => t.Date == new DateOnly(2026, 6, 21)).Count);
        Assert.Equal(Day1, body.FirstClickAt);
        Assert.Equal(Day2, body.LastClickAt);
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
