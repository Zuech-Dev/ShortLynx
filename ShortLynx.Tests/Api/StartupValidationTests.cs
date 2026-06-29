using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using ShortLynx.Services.ApiKeys;

namespace ShortLynx.Tests.Api;

// H3: the host must refuse to start with a missing/default/too-short ApiKey:HmacSecret,
// so a misconfigured deploy fails loudly instead of running under a known key.
public class StartupValidationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public StartupValidationTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public void PlaceholderHmacSecret_FailsStartup()
    {
        var bad = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiKey:HmacSecret"] = ApiKeyOptions.DefaultPlaceholderSecret,
                })));

        var ex = Assert.ThrowsAny<Exception>(() => bad.CreateClient());
        Assert.Contains("HmacSecret", FlattenMessages(ex));
    }

    [Fact]
    public void ShortHmacSecret_FailsStartup()
    {
        var bad = _factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, cfg) =>
                cfg.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApiKey:HmacSecret"] = "too-short",
                })));

        Assert.ThrowsAny<Exception>(() => bad.CreateClient());
    }

    private static string FlattenMessages(Exception ex)
    {
        var messages = new List<string>();
        for (Exception? e = ex; e is not null; e = e.InnerException)
            messages.Add(e.Message);
        return string.Join(" | ", messages);
    }
}
