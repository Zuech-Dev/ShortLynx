using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;

namespace ShortLynx.Tests.Admin;

// Meta calls these two webhooks server-to-server (no browser, no session) when a user disconnects the
// app or requests data deletion via Meta's own UI — see docs/META_APP_SETUP.md. AdminFactory's
// "Meta:AppSecret" test config matches the secret used to sign requests here.
public class ThreadsWebhookTests : IClassFixture<AdminFactory>
{
    private const string AppSecret = "test-meta-app-secret"; // must match AdminFactory's test config
    private readonly AdminFactory _factory;
    public ThreadsWebhookTests(AdminFactory factory) => _factory = factory;

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string BuildSignedRequest(string userId, string secret = AppSecret)
    {
        var json = $$"""{"user_id":"{{userId}}","algorithm":"HMAC-SHA256","issued_at":1735689600}""";
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return $"{Base64UrlEncode(signature)}.{payload}";
    }

    private async Task<Guid> SeedConnectionAsync(string externalAccountId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

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

    private async Task<bool> ConnectionExistsAsync(Guid connectionId)
    {
        using var scope = _factory.Services.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SocialConnectionEntities.AnyAsync(c => c.Id == connectionId);
    }

    // ── Deauthorize (uninstall) ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Deauthorize_ValidSignedRequest_DeletesMatchingConnection()
    {
        var connectionId = await SeedConnectionAsync("17800000000000001");
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["signed_request"] = BuildSignedRequest("17800000000000001"),
        });

        var resp = await _factory.CreateClient().PostAsync("/webhooks/threads/deauthorize", body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(await ConnectionExistsAsync(connectionId));
    }

    [Fact]
    public async Task Deauthorize_TamperedSignature_Returns400_ConnectionSurvives()
    {
        var connectionId = await SeedConnectionAsync("17800000000000002");
        var forged = BuildSignedRequest("17800000000000002", secret: "wrong-secret");
        var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["signed_request"] = forged });

        var resp = await _factory.CreateClient().PostAsync("/webhooks/threads/deauthorize", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.True(await ConnectionExistsAsync(connectionId)); // an invalid signature must delete nothing
    }

    [Fact]
    public async Task Deauthorize_MissingSignedRequestField_Returns400()
    {
        var resp = await _factory.CreateClient().PostAsync("/webhooks/threads/deauthorize",
            new FormUrlEncodedContent(new Dictionary<string, string>()));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Deauthorize_UnknownUserId_Returns200_NoOp()
    {
        // A genuine callback for a user_id we have no connection for must not error — nothing to do.
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["signed_request"] = BuildSignedRequest("99999999999999999"),
        });

        var resp = await _factory.CreateClient().PostAsync("/webhooks/threads/deauthorize", body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // ── Data deletion ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_ValidSignedRequest_DeletesConnection_AndReturnsMetaShape()
    {
        var connectionId = await SeedConnectionAsync("17800000000000003");
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["signed_request"] = BuildSignedRequest("17800000000000003"),
        });

        var resp = await _factory.CreateClient().PostAsync("/webhooks/threads/delete", body);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.False(await ConnectionExistsAsync(connectionId));

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
        var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["signed_request"] = BuildSignedRequest("178", secret: "wrong-secret"),
        });

        var resp = await _factory.CreateClient().PostAsync("/webhooks/threads/delete", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task DeleteStatus_Page_Renders()
    {
        var resp = await _factory.CreateClient().GetAsync("/social/threads/delete-status?id=abc123");

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.Contains("Deletion complete", await resp.Content.ReadAsStringAsync());
    }
}
