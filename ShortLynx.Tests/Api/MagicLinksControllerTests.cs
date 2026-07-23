using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Data.Context;

namespace ShortLynx.Tests.Api;

public class MagicLinksControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public MagicLinksControllerTests(ApiFactory factory) => _factory = factory;

    // ── POST /magic-links ─────────────────────────────────────────────────────

    [Fact]
    public async Task RequestMagicLink_ValidEmail_Returns204()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/magic-links",
            new RequestMagicLinkRequest("valid@example.com"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RequestMagicLink_NoAuthRequired()
    {
        // AllowAnonymous — no Bearer token should still succeed
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/magic-links",
            new RequestMagicLinkRequest("noauth@example.com"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task RequestMagicLink_ActiveUser_SendsEmail()
    {
        await _factory.SeedUserAsync("email-send-check@example.com");
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/magic-links",
            new RequestMagicLinkRequest("email-send-check@example.com"));

        Assert.Contains(_factory.EmailSender.Sent,
            e => e.To == "email-send-check@example.com");
    }

    [Fact]
    public async Task RequestMagicLink_ActiveUser_EmailContainsToken()
    {
        await _factory.SeedUserAsync("token-email@example.com");
        var client = _factory.CreateClient();
        await client.PostAsJsonAsync("/magic-links",
            new RequestMagicLinkRequest("token-email@example.com"));

        var email = _factory.EmailSender.Sent.Last(e => e.To == "token-email@example.com");
        Assert.Contains("token=", email.HtmlBody);
    }

    [Fact]
    public async Task RequestMagicLink_UnknownEmail_DoesNotCreateUserOrSendEmail()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/magic-links",
            new RequestMagicLinkRequest("new-magic-user@example.com"));

        // Still 204 to prevent enumeration, but no user is provisioned and no email is sent.
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(_factory.EmailSender.Sent, e => e.To == "new-magic-user@example.com");

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
        var user = db.UserAccountEntities
            .SingleOrDefault(u => u.Email == "new-magic-user@example.com");

        Assert.Null(user);
    }

    [Fact]
    public async Task RequestMagicLink_InactiveUser_DoesNotSendEmail()
    {
        await _factory.SeedUserAsync("deactivated@example.com", isActive: false);
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/magic-links",
            new RequestMagicLinkRequest("deactivated@example.com"));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.DoesNotContain(_factory.EmailSender.Sent, e => e.To == "deactivated@example.com");
    }

    [Fact]
    public async Task RequestMagicLink_SameEmailTwice_Returns204Both()
    {
        var client = _factory.CreateClient();
        var r1 = await client.PostAsJsonAsync("/magic-links",
            new RequestMagicLinkRequest("repeated@example.com"));
        var r2 = await client.PostAsJsonAsync("/magic-links",
            new RequestMagicLinkRequest("repeated@example.com"));

        Assert.Equal(HttpStatusCode.NoContent, r1.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, r2.StatusCode);
    }

    [Fact]
    public async Task RequestMagicLink_InvalidEmail_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/magic-links",
            new RequestMagicLinkRequest("not-an-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RequestMagicLink_EmptyEmail_Returns400()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/magic-links",
            new RequestMagicLinkRequest(string.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Per-email throttle (H1) ───────────────────────────────────────────────

    [Fact]
    public async Task RequestMagicLink_SameEmailBeyondCap_StopsSendingButStill204()
    {
        await _factory.SeedUserAsync("throttle-target@example.com");
        var client = _factory.CreateClient();
        const string email = "throttle-target@example.com";

        // Six requests for the same address; the IP limiter is set high in tests, so only the
        // per-email cap (MaxActiveTokensPerUser = 3) applies. All return 204 (silent throttle).
        for (var i = 0; i < 6; i++)
        {
            var resp = await client.PostAsJsonAsync("/magic-links", new RequestMagicLinkRequest(email));
            Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);
        }

        Assert.Equal(3, _factory.EmailSender.Sent.Count(e => e.To == email));
    }
}
