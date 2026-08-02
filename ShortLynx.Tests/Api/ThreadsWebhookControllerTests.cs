using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Tests.Api;

// ThreadsWebhookController replaces ShortLynx.Admin's former deauthorize/delete minimal APIs
// (Program.cs) — ported close to verbatim from ShortLynx.Tests/Admin/ThreadsWebhookTests.cs, targeting
// Core's ApiFactory instead of AdminFactory. These calls are unauthenticated server-to-server webhooks
// (no browser, no session), so the interesting behavior is entirely in the HMAC verification.
public class ThreadsWebhookControllerTests : IClassFixture<ApiFactory>
{
    private const string AppSecret = "test-threads-webhook-secret";
    private readonly ApiFactory _factory;
    public ThreadsWebhookControllerTests(ApiFactory factory) => _factory = factory;

    private WebApplicationFactory<ShortLynx.Core.CoreApiEntryPoint> ConfiguredHost()
        => _factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, cfg) =>
            cfg.AddInMemoryCollection(new Dictionary<string, string?> { ["Threads:AppSecret"] = AppSecret })));

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string BuildSignedRequest(string userId, string secret = AppSecret)
    {
        var json = $$"""{"user_id":"{{userId}}","algorithm":"HMAC-SHA256","issued_at":1735689600}""";
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return $"{Base64UrlEncode(signature)}.{payload}";
    }

    private async Task<Guid> SeedConnectionAsync(
        WebApplicationFactory<ShortLynx.Core.CoreApiEntryPoint> host, string externalAccountId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();

        var account = new AccountEntity { Id = Guid.CreateVersion7(), Name = "Test", CreatedAt = DateTimeOffset.UtcNow, IsActive = true };
        var connection = new SocialConnectionEntity
        {
            Id = Guid.CreateVersion7(), AccountId = account.Id, Platform = SocialPlatform.Threads,
            ExternalAccountId = externalAccountId, Handle = "@me", AccessTokenProtected = "enc:x",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        db.AccountEntities.Add(account);
        db.SocialConnectionEntities.Add(connection);
        await db.SaveChangesAsync();
        return connection.Id;
    }

    private async Task<bool> ConnectionExistsAsync(
        WebApplicationFactory<ShortLynx.Core.CoreApiEntryPoint> host, Guid connectionId)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
        return await db.SocialConnectionEntities.AnyAsync(c => c.Id == connectionId);
    }

    // ── Deauthorize (uninstall) ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deauthorize_ValidSignedRequest_DeletesMatchingConnection()
    {
        var host = ConfiguredHost();
        var connectionId = await SeedConnectionAsync(host, "17800000000000001");
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["signed_request"] = BuildSignedRequest("17800000000000001"),
        });

        var resp = await host.CreateClient().PostAsync("/webhooks/threads/deauthorize", body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(await ConnectionExistsAsync(host, connectionId));
    }

    [Fact]
    public async Task Deauthorize_TamperedSignature_Returns400_ConnectionSurvives()
    {
        var host = ConfiguredHost();
        var connectionId = await SeedConnectionAsync(host, "17800000000000002");
        var forged = BuildSignedRequest("17800000000000002", secret: "wrong-secret");
        var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["signed_request"] = forged });

        var resp = await host.CreateClient().PostAsync("/webhooks/threads/deauthorize", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.True(await ConnectionExistsAsync(host, connectionId)); // an invalid signature must delete nothing
    }

    [Fact]
    public async Task Deauthorize_MissingSignedRequestField_Returns400()
    {
        var host = ConfiguredHost();

        var resp = await host.CreateClient().PostAsync("/webhooks/threads/deauthorize",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Deauthorize_UnknownUserId_Returns200_NoOp()
    {
        var host = ConfiguredHost();
        // A genuine callback for a user_id we have no connection for must not error — nothing to do.
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["signed_request"] = BuildSignedRequest("99999999999999999"),
        });

        var resp = await host.CreateClient().PostAsync("/webhooks/threads/deauthorize", body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Data deletion ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ValidSignedRequest_DeletesConnection_AndReturnsMetaShape()
    {
        var host = ConfiguredHost();
        var connectionId = await SeedConnectionAsync(host, "17800000000000003");
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["signed_request"] = BuildSignedRequest("17800000000000003"),
        });

        var resp = await host.CreateClient().PostAsync("/webhooks/threads/delete", body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(await ConnectionExistsAsync(host, connectionId));

        using var json = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var url = json.RootElement.GetProperty("url").GetString();
        var confirmationCode = json.RootElement.GetProperty("confirmation_code").GetString();
        Assert.False(string.IsNullOrWhiteSpace(confirmationCode));
        Assert.Contains("/social/threads/delete-status?id=", url);
        Assert.Contains(confirmationCode!, url);
    }

    [Fact]
    public async Task Delete_TamperedSignature_Returns400()
    {
        var host = ConfiguredHost();
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["signed_request"] = BuildSignedRequest("178", secret: "wrong-secret"),
        });

        var resp = await host.CreateClient().PostAsync("/webhooks/threads/delete", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteStatus_Page_Renders()
    {
        var host = ConfiguredHost();

        var resp = await host.CreateClient().GetAsync("/social/threads/delete-status?id=abc123");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Deletion complete", await resp.Content.ReadAsStringAsync());
    }
}
