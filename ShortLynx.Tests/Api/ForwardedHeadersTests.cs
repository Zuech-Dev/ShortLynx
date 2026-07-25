using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;

namespace ShortLynx.Tests.Api;

/// <summary>
/// Regression cover for the production bug where per-IP rate limiting didn't partition real clients
/// together: the app cleared its trusted-proxy list, so the ForwardedHeaders middleware silently
/// dropped X-Forwarded-For and RemoteIpAddress fell back to an internal address that varied per
/// connection. With the edge hop trusted, the limiter must key on the forwarded client IP.
///
/// The first two tests here pass a SINGLE forwarded entry, which is why they could not catch the
/// follow-on production bug fixed 2026-07-25 (ForwardLimit=1 against a two-hop edge). A test that
/// synthesises the header the environment is supposed to supply verifies the parsing, not the
/// configuration — see <see cref="SameClientBehindARotatingEdgeHop_StillSharesThePartition"/>, which
/// models the real shape instead.
/// </summary>
public class ForwardedHeadersTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public ForwardedHeadersTests(ApiFactory factory) => _factory = factory;

    private HttpClient LowRefreshLimitClient(int? forwardLimit = null) => _factory.WithWebHostBuilder(b =>
    {
        b.ConfigureAppConfiguration((_, cfg) =>
        {
            var settings = new Dictionary<string, string?>
            {
                ["RateLimit:RefreshPermitLimit"] = "3",
                ["RateLimit:RefreshWindowSeconds"] = "300",
            };
            if (forwardLimit is not null)
                settings["ForwardedHeaders:ForwardLimit"] = forwardLimit.Value.ToString();
            cfg.AddInMemoryCollection(settings);
        });
    }).CreateClient();

    [Fact]
    public async Task SameForwardedClientIp_SharesTheRateLimitPartition()
    {
        var client = LowRefreshLimitClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh")
            {
                Content = JsonContent.Create(new { refreshToken = $"bogus-{i}" }),
            };
            req.Headers.Add("X-Forwarded-For", "203.0.113.5");
            statuses.Add((await client.SendAsync(req)).StatusCode);
        }

        // 3 bogus tokens get through as 401; the rest are throttled — the forwarded IP is the key.
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task DistinctForwardedClientIps_DoNotShareThePartition()
    {
        var client = LowRefreshLimitClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh")
            {
                Content = JsonContent.Create(new { refreshToken = $"bogus-{i}" }),
            };
            // A different client IP each time — each gets its own window, so none is throttled.
            req.Headers.Add("X-Forwarded-For", $"198.51.100.{i + 1}");
            statuses.Add((await client.SendAsync(req)).StatusCode);
        }

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
    }

    // The real production header shape: the client, then the edge's own entry appended on the right.
    // The edge address deliberately ROTATES per request, as Railway's does — that rotation is the whole
    // reason the bug was invisible. With a constant second entry both a correct and a broken
    // ForwardLimit would key on *something* stable and the test would pass either way, proving nothing.
    private static string TwoHopHeader(string client, int requestIndex) => $"{client}, 10.0.{requestIndex}.{requestIndex + 1}";

    private async Task<List<HttpStatusCode>> SixRefreshAttempts(HttpClient client, Func<int, string> xff)
    {
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/auth/refresh")
            {
                Content = JsonContent.Create(new { refreshToken = $"bogus-{i}" }),
            };
            req.Headers.Add("X-Forwarded-For", xff(i));
            statuses.Add((await client.SendAsync(req)).StatusCode);
        }
        return statuses;
    }

    [Fact]
    public async Task SameClientBehindARotatingEdgeHop_StillSharesThePartition()
    {
        // Regression test for the 2026-07-25 production bug. The edge appends its own hop, so the
        // client is the SECOND entry from the right; ForwardLimit must step past the edge to reach it.
        // Under the previous ForwardLimit=1 the limiter keyed on the rotating edge address, every
        // request landed in its own partition, and nothing was ever throttled.
        var statuses = await SixRefreshAttempts(
            LowRefreshLimitClient(), i => TwoHopHeader("203.0.113.5", i));

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }

    [Fact]
    public async Task ForwardLimit1_AgainstATwoHopEdge_FailsToPartition()
    {
        // Pins the broken behaviour to the misconfiguration itself rather than to the app, so the
        // regression above can't be "fixed" by quietly loosening the limiter. With only one hop trusted
        // the rotating edge address becomes the key and no burst is ever throttled.
        var statuses = await SixRefreshAttempts(
            LowRefreshLimitClient(forwardLimit: 1), i => TwoHopHeader("203.0.113.5", i));

        Assert.All(statuses, s => Assert.Equal(HttpStatusCode.Unauthorized, s));
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, statuses);
    }

    [Fact]
    public async Task ForwardedClientIp_IsNotTakenFromAClientForgedLeftmostEntry()
    {
        // Security property of using an exact hop count rather than "unlimited": a client that injects
        // its own X-Forwarded-For entry only adds to the LEFT, beyond the trusted hop count, so it
        // cannot choose the address the limiter keys on. Two attackers forging distinct leftmost
        // entries behind the same real client IP must still share one partition and get throttled.
        var statuses = await SixRefreshAttempts(
            LowRefreshLimitClient(), i => $"9.9.9.{i}, 203.0.113.9, 10.0.0.{i}");

        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.TooManyRequests));
    }
}
