using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Services.Entitlements;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Api;

/// <summary>
/// The plan gates added to the domain/campaign/API-key/member services, asserted end to end: a denial
/// must surface as 402 (via <c>EntitlementExceptionFilter</c> on the controller base classes) rather than
/// escaping as a 500, and the OSS default must still allow everything.
/// </summary>
public class PlanGatesApiTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public PlanGatesApiTests(ApiFactory factory) => _factory = factory;

    public static TheoryData<string, string, string> GatedCreates => new()
    {
        { "/me/domains", """{"domain":"go.gated-{0}.test"}""", nameof(PlanFeature.CustomDomains) },
        { "/me/campaigns", """{"name":"Launch"}""", nameof(PlanFeature.Campaigns) },
        { "/me/api-keys", """{"name":"key","scopes":["links:write"]}""", nameof(PlanFeature.ApiAccess) },
        { "/me/members", """{"email":"invitee-{0}@example.com","role":"Member"}""", "Seats" },
    };

    private async Task<HttpClient> ClientWithAsync(IEntitlements entitlements)
    {
        var (token, _, _) = await _factory.SeedMemberTokenAsync();
        var host = _factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton(entitlements)));
        var session = await (await host.CreateClient().PostAsJsonAsync("/auth/session", new CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<SessionResponse>();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {session!.AccessToken}");
        return client;
    }

    private static StringContent Json(string template)
        => new(template.Replace("{0}", Guid.NewGuid().ToString("N")), Encoding.UTF8, "application/json");

    [Theory]
    [MemberData(nameof(GatedCreates))]
    public async Task Create_WhenPlanDenies_Returns402WithMessage(string path, string body, string gate)
    {
        var deny = gate == "Seats"
            ? new FakeEntitlements { AllowMembers = false }
            : FakeEntitlements.Without(Enum.Parse<PlanFeature>(gate));
        var client = await ClientWithAsync(deny);

        var resp = await client.PostAsync(path, Json(body));

        Assert.Equal(HttpStatusCode.PaymentRequired, resp.StatusCode);
        var error = await resp.Content.ReadFromJsonAsync<Dictionary<string, string>>();
        Assert.Contains("plan", error!["error"]);
    }

    [Theory]
    [MemberData(nameof(GatedCreates))]
    public async Task Create_UnderDefaultUnlimited_Succeeds(string path, string body, string _)
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var resp = await client.PostAsync(path, Json(body));

        Assert.True(resp.IsSuccessStatusCode, $"{path} → {(int)resp.StatusCode}: {await resp.Content.ReadAsStringAsync()}");
    }

    [Fact]
    public async Task Entitlements_ReflectsThePlan_ForAnyMember()
    {
        var client = await ClientWithAsync(new FakeEntitlements
        {
            AllowCustomCodes = false,
            AllowCustomDomainSlot = false,
            RetentionDays = 30,
            DisabledFeatures = { PlanFeature.UserAttributedLinks, PlanFeature.Campaigns },
        });

        var e = await client.GetFromJsonAsync<EntitlementsResponse>("/me/entitlements");

        Assert.NotNull(e);
        Assert.True(e.CanCreateLink);
        Assert.False(e.CustomCodes);
        Assert.False(e.UserAttributedLinks);
        Assert.False(e.Campaigns);
        Assert.True(e.CustomDomains);
        Assert.False(e.CanAddCustomDomain);
        Assert.True(e.SocialPublishing);
        Assert.Equal(30, e.RetentionDays);
    }

    [Fact]
    public async Task Entitlements_SelfHostDefault_AllowsEverything()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var e = await client.GetFromJsonAsync<EntitlementsResponse>("/me/entitlements");

        Assert.NotNull(e);
        Assert.True(e.CanCreateLink && e.CustomCodes && e.UserAttributedLinks && e.Campaigns && e.CustomDomains
                    && e.CanAddCustomDomain && e.SocialPublishing && e.ApiAccess && e.Conversions && e.CanAddMember);
        Assert.Null(e.RetentionDays);
    }

    [Fact]
    public async Task LinkAnalytics_HidesClicksOlderThanRetention()
    {
        // End to end through MeLinksController: a 30-day plan sees only the recent click.
        var (token, _, accountId) = await _factory.SeedMemberTokenAsync();
        Guid linkId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShortLynx.Data.Context.ShortLynxDbContext>();
            var link = EntityFactory.AnonymousLink(accountId);
            var code = EntityFactory.ShortCode(link.Id, $"r{Guid.NewGuid():N}"[..8]);
            var recent = EntityFactory.Visit(code.Id);
            recent.ClickedAt = DateTimeOffset.UtcNow.AddDays(-2);
            var old = EntityFactory.Visit(code.Id);
            old.ClickedAt = DateTimeOffset.UtcNow.AddDays(-60);
            db.AddRange(link, code, recent, old);
            await db.SaveChangesAsync();
            linkId = link.Id;
        }

        var host = _factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddSingleton<IEntitlements>(new FakeEntitlements { RetentionDays = 30 })));
        var session = await (await host.CreateClient().PostAsJsonAsync("/auth/session", new CreateSessionRequest(token)))
            .Content.ReadFromJsonAsync<SessionResponse>();
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {session!.AccessToken}");

        var analytics = await client.GetFromJsonAsync<LinkAnalyticsResponse>($"/me/links/{linkId}/analytics");

        Assert.Equal(1, analytics!.TotalClicks);
    }
}
