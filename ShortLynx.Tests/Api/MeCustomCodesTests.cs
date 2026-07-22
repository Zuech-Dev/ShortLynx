using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Entitlements;

namespace ShortLynx.Tests.Api;

public class MeCustomCodesTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeCustomCodesTests(ApiFactory factory) => _factory = factory;

    private sealed class DenyCustomCodes : IEntitlements
    {
        public Task<bool> CanCreateLinkAsync(Guid a, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanCreateCustomCodeAsync(Guid a, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> IsFeatureEnabledAsync(Guid a, PlanFeature f, CancellationToken ct = default) => Task.FromResult(true);
    }

    private sealed record CheckResponse(bool Available, string Status, string? Reason);

    private static async Task<CheckResponse> CheckAsync(HttpClient client, string code)
        => (await client.GetFromJsonAsync<CheckResponse>($"/me/custom-code/check?code={Uri.EscapeDataString(code)}"))!;

    [Fact]
    public async Task Check_NoSession_Returns401()
    {
        var resp = await _factory.CreateClient().GetAsync("/me/custom-code/check?code=my-code-1");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Check_ValidUnusedCode_IsAvailable()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var r = await CheckAsync(client, "my-uniq-code");
        Assert.True(r.Available);
        Assert.Equal("Available", r.Status);
    }

    [Fact]
    public async Task Check_InvalidCode_ReportsInvalidWithReason()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var r = await CheckAsync(client, "short"); // < 8
        Assert.False(r.Available);
        Assert.Equal("Invalid", r.Status);
        Assert.Contains("at least", r.Reason);
    }

    [Fact]
    public async Task Check_ExistingCode_IsTaken()
    {
        var (client, _, accountId) = await _factory.CreateSessionClientAsync();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
            var link = new LinkEntity
            {
                Id = Guid.CreateVersion7(), OriginalUrl = "https://example.com", AccountId = accountId,
                Mode = ShortLynx.Data.Enums.LinkMode.Anonymous, CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            };
            db.Add(link);
            db.Add(new ShortCodeEntity
            {
                Id = Guid.CreateVersion7(), LinkId = link.Id, Code = "taken-code", IsCustom = true,
                CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
            });
            await db.SaveChangesAsync();
        }

        var r = await CheckAsync(client, "taken-code");
        Assert.False(r.Available);
        Assert.Equal("Taken", r.Status);
    }

    [Fact]
    public async Task Check_WhenPlanDeniesCustomCodes_Returns402()
    {
        var (token, _, _) = await _factory.SeedMemberTokenAsync();
        var host = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IEntitlements>(new DenyCustomCodes())));

        var session = await (await host.CreateClient().PostAsJsonAsync(
                "/auth/session", new CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<SessionResponse>();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {session!.AccessToken}");

        var resp = await client.GetAsync("/me/custom-code/check?code=my-uniq-code");
        Assert.Equal(HttpStatusCode.PaymentRequired, resp.StatusCode);
    }

    [Fact]
    public async Task Check_ExceedingRateLimit_Returns429()
    {
        var (token, _, _) = await _factory.SeedMemberTokenAsync();
        var host = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimit:CustomCodeCheckPermitLimit"] = "3",
                ["RateLimit:CustomCodeCheckWindowSeconds"] = "60",
            })));

        var session = await (await host.CreateClient().PostAsJsonAsync(
                "/auth/session", new CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<SessionResponse>();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {session!.AccessToken}");

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
            statuses.Add((await client.GetAsync($"/me/custom-code/check?code=code-num-{i}")).StatusCode);

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.OK));
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
