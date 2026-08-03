using Sentry;
using ShortLynx.Services.Observability;

namespace ShortLynx.Tests.Services.Observability;

public class ObservabilityExtensionsTests
{
    [Fact]
    public void ScrubSensitiveData_RedactsQueryString_WhenPresent()
    {
        var @event = new SentryEvent();
        @event.Request.QueryString = "token=super-secret-magic-link-token";

        var result = ObservabilityExtensions.ScrubSensitiveData(@event);

        Assert.Equal("[Scrubbed]", result!.Request.QueryString);
    }

    [Fact]
    public void ScrubSensitiveData_LeavesQueryStringAlone_WhenAbsent()
    {
        var @event = new SentryEvent();

        var result = ObservabilityExtensions.ScrubSensitiveData(@event);

        Assert.True(string.IsNullOrEmpty(result!.Request.QueryString));
    }

    [Theory]
    [InlineData("Authorization")]
    [InlineData("X-Csrf-Token")]
    [InlineData("X-Api-Key")]
    [InlineData("Cookie")]
    public void ScrubSensitiveData_RemovesSensitiveHeader(string header)
    {
        var @event = new SentryEvent();
        @event.Request.Headers.Add(header, "some-sensitive-value");

        var result = ObservabilityExtensions.ScrubSensitiveData(@event);

        Assert.False(result!.Request.Headers.ContainsKey(header));
    }

    [Fact]
    public void ScrubSensitiveData_LeavesNonSensitiveHeadersIntact()
    {
        var @event = new SentryEvent();
        @event.Request.Headers.Add("User-Agent", "test-agent/1.0");
        @event.Request.Headers.Add("Authorization", "Bearer some-token");

        var result = ObservabilityExtensions.ScrubSensitiveData(@event);

        Assert.Equal("test-agent/1.0", result!.Request.Headers["User-Agent"]);
        Assert.False(result.Request.Headers.ContainsKey("Authorization"));
    }

    [Fact]
    public void ScrubSensitiveData_ReturnsSameEvent_NeverDropsIt()
    {
        // A null return here would silently drop the whole event -- that's a "swallow the incident by
        // accident" bug, not a privacy win, so this is worth pinning down explicitly.
        var @event = new SentryEvent();

        var result = ObservabilityExtensions.ScrubSensitiveData(@event);

        Assert.Same(@event, result);
    }
}
