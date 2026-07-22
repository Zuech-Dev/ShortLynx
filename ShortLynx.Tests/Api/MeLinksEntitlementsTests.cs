using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Services.Entitlements;

namespace ShortLynx.Tests.Api;

public class MeLinksEntitlementsTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeLinksEntitlementsTests(ApiFactory factory) => _factory = factory;

    private sealed class DenyEntitlements : IEntitlements
    {
        public Task<bool> CanCreateLinkAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> CanCreateCustomCodeAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(false);
        public Task<bool> IsFeatureEnabledAsync(Guid accountId, PlanFeature feature, CancellationToken ct = default) => Task.FromResult(false);
    }

    [Fact]
    public async Task CreateLink_WhenPlanDenies_Returns402()
    {
        // Seed a real user/account on the shared test DB, then talk to a host whose IEntitlements denies.
        var (token, _, _) = await _factory.SeedMemberTokenAsync();
        var host = _factory.WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<IEntitlements>(new DenyEntitlements())));

        var session = await (await host.CreateClient().PostAsJsonAsync(
                "/auth/session", new CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<SessionResponse>();

        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {session!.AccessToken}");

        var resp = await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com"));
        Assert.Equal(HttpStatusCode.PaymentRequired, resp.StatusCode);
    }

    [Fact]
    public async Task CreateLink_UnderDefaultUnlimited_Succeeds()
    {
        // The OSS default (UnlimitedEntitlements) must never block — self-host stays free/complete.
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var resp = await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com"));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
    }
}
