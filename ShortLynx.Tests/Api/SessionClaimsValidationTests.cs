using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Auth;

namespace ShortLynx.Tests.Api;

/// <summary>
/// A validly-signed session token that lacks the account claim (e.g. an allowlisted user with no
/// membership, or a hand-crafted token) must yield 401 on the /me/* surface — not a 500 from the
/// AccountId claim parse. Covers the reads too, where the write-only role filter doesn't run.
/// </summary>
public class SessionClaimsValidationTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public SessionClaimsValidationTests(ApiFactory factory) => _factory = factory;

    private async Task<string> ClaimlessAccessTokenAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShortLynxDbContext>();
        var user = new UserAccountEntity
        {
            Id = Guid.CreateVersion7(), Email = $"{Guid.NewGuid():N}@example.com",
            CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
        };
        db.Add(user);
        await db.SaveChangesAsync();

        // No account, no role → the access token carries sub but no account_id claim.
        var tokens = await scope.ServiceProvider.GetRequiredService<IUserSessionService>()
            .IssueAsync(user, accountId: null, role: null);
        return tokens.AccessToken;
    }

    private HttpClient WithToken(string accessToken)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {accessToken}");
        return client;
    }

    [Theory]
    [InlineData("/me")]              // GET read — not covered by the write-only role filter
    [InlineData("/me/links")]       // GET list
    [InlineData("/me/api-keys")]    // GET list
    public async Task ClaimlessToken_On_Reads_Returns401_Not500(string path)
    {
        var client = WithToken(await ClaimlessAccessTokenAsync());
        var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ClaimlessToken_On_Write_Returns401()
    {
        var client = WithToken(await ClaimlessAccessTokenAsync());
        var response = await client.PostAsJsonAsync("/me/links", new { url = "https://example.com" });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
