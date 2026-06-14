using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using ShortLynx.Core.Models.Requests;

namespace ShortLynx.Tests.Api;

public class RateLimitTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public RateLimitTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task MagicLinks_ExceedingIpLimit_Returns429()
    {
        // Override the (test-default high) limit with a low one for this host instance.
        var client = _factory.WithWebHostBuilder(b =>
        {
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["RateLimit:MagicLinkPermitLimit"] = "3",
                    ["RateLimit:MagicLinkWindowSeconds"] = "300",
                }));
        }).CreateClient();

        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 6; i++)
        {
            // Distinct emails so the per-email throttle doesn't mask the IP limiter.
            var resp = await client.PostAsJsonAsync("/magic-links",
                new RequestMagicLinkRequest($"rl-{i}@example.com"));
            statuses.Add(resp.StatusCode);
        }

        Assert.Contains(HttpStatusCode.TooManyRequests, statuses);
        Assert.Equal(3, statuses.Count(s => s == HttpStatusCode.NoContent));
    }
}
