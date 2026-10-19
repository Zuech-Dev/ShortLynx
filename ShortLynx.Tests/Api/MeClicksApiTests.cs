using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Tests.Api;

public class MeClicksApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeClicksApiTests(ApiFactory factory) => _factory = factory;

    private static string Iso(DateTimeOffset t) => Uri.EscapeDataString(t.ToString("O"));

    private async Task<(HttpClient Client, DateTimeOffset Base)> SeedAsync()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var link = await (await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com")))
            .Content.ReadFromJsonAsync<LinkResponse>();
        var t0 = DateTimeOffset.UtcNow.AddDays(-2);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
        var scId = await db.ShortCodeEntities.Where(s => s.LinkId == link!.Id).Select(s => s.Id).FirstAsync();
        (DeviceType Device, int Hours)[] seed =
        [
            (DeviceType.Mobile, 1), (DeviceType.SuspectedAutomated, 2), (DeviceType.Bot, 3),
            (DeviceType.Desktop, 30),
        ];
        foreach (var (device, hours) in seed)
            db.VisitEntities.Add(new VisitEntity
            {
                Id = Guid.CreateVersion7(), ShortCodeId = scId, HashedIp = $"ip{hours}", Device = device,
                ClickedAt = t0.AddHours(hours),
            });
        await db.SaveChangesAsync();
        return (client, t0);
    }

    private static async Task<string[]> DevicesAsync(HttpClient client, string query)
    {
        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/me/clicks?{query}"));
        return doc.RootElement.GetProperty("clicks").EnumerateArray()
            .Select(c => c.GetProperty("device").GetString()!).ToArray();
    }

    [Fact]
    public async Task HumanOnly_LeavesOutBotsAndScanners()
    {
        var (client, t0) = await SeedAsync();

        Assert.Equal(["Mobile", "SuspectedAutomated", "Bot", "Desktop"],
            await DevicesAsync(client, $"since={Iso(t0)}"));
        Assert.Equal(["Mobile", "Desktop"],
            await DevicesAsync(client, $"since={Iso(t0)}&humanOnly=true"));
    }

    [Fact]
    public async Task Until_BoundsTheWindow_AndIsEchoed()
    {
        var (client, t0) = await SeedAsync();
        var until = t0.AddHours(24);

        Assert.Equal(["Mobile", "SuspectedAutomated", "Bot"],
            await DevicesAsync(client, $"since={Iso(t0)}&until={Iso(until)}"));

        using var doc = JsonDocument.Parse(await client.GetStringAsync($"/me/clicks?since={Iso(t0)}&until={Iso(until)}"));
        Assert.Equal(until, doc.RootElement.GetProperty("until").GetDateTimeOffset());
    }

    [Fact]
    public async Task UntilNotAfterSince_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var t = DateTimeOffset.UtcNow.AddDays(-1);
        var resp = await client.GetAsync($"/me/clicks?since={Iso(t)}&until={Iso(t)}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
