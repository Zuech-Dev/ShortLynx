using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Visits;

namespace ShortLynx.Tests.Api;

public class AdminMaintenanceControllerTests : IClassFixture<ApiFactory>
{
    private const string Path = "/admin/maintenance/reclassify-link-scanners";
    private readonly ApiFactory _factory;
    public AdminMaintenanceControllerTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task WithoutSession_Returns401()
    {
        var resp = await _factory.CreateClient().PostAsJsonAsync(Path, new { before = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task AsAccountOwner_Returns403()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var resp = await client.PostAsJsonAsync(Path, new { before = DateTimeOffset.UtcNow });
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task MissingOrFutureCutoff_Returns400()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(Path, new { dryRun = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PostAsJsonAsync(Path, new { before = DateTimeOffset.UtcNow.AddDays(1) })).StatusCode);
    }

    [Fact]
    public async Task DryRunByDefault_ThenApplies()
    {
        var (client, _) = await _factory.CreateAdminSessionClientAsync();
        var id = Guid.CreateVersion7();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            db.VisitEntities.Add(new VisitEntity
            {
                Id = id, HashedIp = "ip", Device = DeviceType.Desktop, NavigationType = null,
                ClickedAt = DateTimeOffset.UtcNow.AddDays(-2),
            });
            await db.SaveChangesAsync();
        }
        var before = DateTimeOffset.UtcNow.AddDays(-1);

        var dry = await (await client.PostAsJsonAsync(Path, new { before }))
            .Content.ReadFromJsonAsync<ScannerReclassifyResult>();
        Assert.True(dry!.DryRun);
        Assert.True(dry.Visits >= 1);
        Assert.Equal(DeviceType.Desktop, await DeviceAsync(id));

        var applied = await (await client.PostAsJsonAsync(Path, new { before, dryRun = false }))
            .Content.ReadFromJsonAsync<ScannerReclassifyResult>();
        Assert.False(applied!.DryRun);
        Assert.Equal(DeviceType.SuspectedAutomated, await DeviceAsync(id));
    }

    private async Task<DeviceType> DeviceAsync(Guid id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
        return await db.VisitEntities.Where(v => v.Id == id).Select(v => v.Device).SingleAsync();
    }
}
