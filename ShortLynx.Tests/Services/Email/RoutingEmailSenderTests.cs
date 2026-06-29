using Microsoft.Extensions.Options;
using ShortLynx.Services.Email;

namespace ShortLynx.Tests.Services.Email;

public class RoutingEmailSenderTests
{
    private sealed class CapturingSender : IEmailSender
    {
        public readonly List<string> Recipients = [];
        public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        {
            Recipients.Add(to);
            return Task.CompletedTask;
        }
    }

    [Theory]
    [InlineData("me@zuech.dev", true)]
    [InlineData("ME@ZUECH.DEV", true)]   // case-insensitive
    [InlineData("teammate@example.com", false)]
    [InlineData("no-at-sign", false)]
    public void IsDeliverable_MatchesConfiguredDomains(string email, bool expected)
    {
        var opts = new EmailDeliveryOptions { DeliverableDomains = ["zuech.dev"] };
        Assert.Equal(expected, opts.IsDeliverable(email));
    }

    [Fact]
    public async Task Routes_DeliverableToPrimary_OthersToFallback()
    {
        var primary = new CapturingSender();
        var fallback = new CapturingSender();
        var opts = Options.Create(new EmailDeliveryOptions { DeliverableDomains = ["zuech.dev"] });
        var sut = new RoutingEmailSender(primary, fallback, opts);

        await sut.SendAsync("owner@zuech.dev", "s", "<a href=\"x\">link</a>");
        await sut.SendAsync("invited@example.com", "s", "<a href=\"y\">link</a>");

        Assert.Equal(["owner@zuech.dev"], primary.Recipients);
        Assert.Equal(["invited@example.com"], fallback.Recipients);
    }
}
