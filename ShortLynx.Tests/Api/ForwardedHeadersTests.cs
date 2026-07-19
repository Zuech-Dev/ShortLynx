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
/// </summary>
public class ForwardedHeadersTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public ForwardedHeadersTests(ApiFactory factory) => _factory = factory;

    private HttpClient LowRefreshLimitClient() => _factory.WithWebHostBuilder(b =>
    {
        b.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RateLimit:RefreshPermitLimit"] = "3",
                ["RateLimit:RefreshWindowSeconds"] = "300",
            }));
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
}
