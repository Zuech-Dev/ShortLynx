using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Services.Entitlements;

namespace ShortLynx.Tests.Api;

/// <summary>End-to-end custom-code creation through POST /me/links (Anonymous mode only).</summary>
public class MeLinksCustomCodeTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeLinksCustomCodeTests(ApiFactory factory) => _factory = factory;

    private sealed class DenyCustomCodes : IEntitlements
    {
        public Task<bool> CanCreateLinkAsync(Guid a, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> CanCreateCustomCodeAsync(Guid a, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> IsFeatureEnabledAsync(Guid a, PlanFeature f, CancellationToken ct = default) => Task.FromResult(true);
    }

    [Fact]
    public async Task Create_WithCustomCode_MintsItAndFlagsCustom()
    {
        var (client, _, accountId) = await _factory.CreateSessionClientAsync();

        var resp = await client.PostAsJsonAsync("/me/links",
            new CreateMyLinkRequest("https://example.com/x", CustomCode: "my-code-12"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal("my-code-12", body!.ShortCode);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
        var sc = await db.ShortCodeEntities.SingleAsync(x => x.Code == "my-code-12");
        Assert.True(sc.IsCustom);
    }

    [Fact]
    public async Task Create_UppercaseCustomCode_IsNormalizedToLowercase()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var resp = await client.PostAsJsonAsync("/me/links",
            new CreateMyLinkRequest("https://example.com/u", CustomCode: "Up-Code-99"));
        var body = await resp.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.Equal("up-code-99", body!.ShortCode);
    }

    [Fact]
    public async Task Create_TakenCustomCode_Returns409_AndCreatesNoOrphanLink()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/me/links",
            new CreateMyLinkRequest("https://example.com/1", CustomCode: "clash-code-1"))).StatusCode);

        var before = await LinkCount();
        var resp = await client.PostAsJsonAsync("/me/links",
            new CreateMyLinkRequest("https://example.com/2", CustomCode: "clash-code-1"));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        Assert.Equal(before, await LinkCount()); // atomic — no orphan link from the failed mint
    }

    [Fact]
    public async Task Create_InvalidCustomCode_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var resp = await client.PostAsJsonAsync("/me/links",
            new CreateMyLinkRequest("https://example.com/i", CustomCode: "no")); // too short
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_CustomCode_OnUserAttributed_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var resp = await client.PostAsJsonAsync("/me/links",
            new CreateMyLinkRequest("https://example.com/m", Mode: "UserAttributed", CustomCode: "my-code-12"));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("anonymous", await resp.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Create_CustomCode_WhenPlanDenies_Returns402()
    {
        var (token, _, _) = await _factory.SeedMemberTokenAsync();
        var host = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IEntitlements>(new DenyCustomCodes())));
        var session = await (await host.CreateClient().PostAsJsonAsync(
                "/auth/session", new CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<SessionResponse>();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {session!.AccessToken}");

        var resp = await client.PostAsJsonAsync("/me/links",
            new CreateMyLinkRequest("https://example.com/d", CustomCode: "paid-code-1"));
        Assert.Equal(HttpStatusCode.PaymentRequired, resp.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutCustomCode_StillGetsRandomCode()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var resp = await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com/r"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.False(string.IsNullOrEmpty(body!.ShortCode));
    }

    private async Task<int> LinkCount()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
        return await db.LinkEntities.CountAsync();
    }
}
