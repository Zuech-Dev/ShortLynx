using System.Net;
using System.Net.Http.Json;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;

namespace ShortLynx.Tests.Api;

public class ApiKeysControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    private const string AdminSecret = "test-admin-secret-value";

    public ApiKeysControllerTests(ApiFactory factory) => _factory = factory;

    private HttpClient CreateAdminClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {AdminSecret}");
        return client;
    }

    // ── Authentication ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateApiKey_NoAuthHeader_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api-keys", new CreateApiKeyRequest("test", [], Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateApiKey_WrongAdminSecret_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer wrong-secret");
        var response = await client.PostAsJsonAsync("/api-keys", new CreateApiKeyRequest("test", [], Guid.NewGuid()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── POST /api-keys ────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateApiKey_ValidRequest_Returns200WithKey()
    {
        var client = CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api-keys",
            new CreateApiKeyRequest("integration-test-key", ["links:read", "links:write"], await _factory.SeedAccountAsync()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiKeyResponse>();
        Assert.NotNull(body);
        Assert.Equal("integration-test-key", body.Name);
        Assert.NotEmpty(body.PlaintextKey);
        Assert.Equal(64, body.PlaintextKey.Length); // 32-byte hex
    }

    [Fact]
    public async Task CreateApiKey_ValidRequest_ReturnsScopesInResponse()
    {
        var client = CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api-keys",
            new CreateApiKeyRequest("scoped-key", ["links:read", "analytics:read"], await _factory.SeedAccountAsync()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApiKeyResponse>();
        Assert.NotNull(body);
        Assert.Contains("links:read", body.Scopes);
        Assert.Contains("analytics:read", body.Scopes);
    }

    [Fact]
    public async Task CreateApiKey_PlaintextKeyWorksForApiAuth()
    {
        // Create an API key via the admin endpoint.
        var adminClient = CreateAdminClient();
        var keyResponse = await (await adminClient.PostAsJsonAsync("/api-keys",
            new CreateApiKeyRequest("auto-auth-test-key", ["links:read", "links:write"], await _factory.SeedAccountAsync())))
            .Content.ReadFromJsonAsync<ApiKeyResponse>();

        Assert.NotNull(keyResponse);

        // Use the returned plaintext key to call the links API.
        var apiClient = _factory.CreateClient();
        apiClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {keyResponse.PlaintextKey}");

        var listResponse = await apiClient.GetAsync("/links");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
    }

    [Fact]
    public async Task CreateApiKey_MissingName_Returns400()
    {
        var client = CreateAdminClient();
        var response = await client.PostAsJsonAsync("/api-keys", new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── GET /links ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListLinks_NewKey_ReturnsEmptyArray()
    {
        var adminClient = CreateAdminClient();
        var keyResponse = await (await adminClient.PostAsJsonAsync("/api-keys",
            new CreateApiKeyRequest("empty-links-key", ["links:read"], await _factory.SeedAccountAsync())))
            .Content.ReadFromJsonAsync<ApiKeyResponse>();

        var apiClient = _factory.CreateClient();
        apiClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {keyResponse!.PlaintextKey}");

        var response = await apiClient.GetAsync("/links");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<List<LinkResponse>>();
        Assert.NotNull(body);
        Assert.Empty(body);
    }

    [Fact]
    public async Task ListLinks_ReturnsOwnLinksOnly()
    {
        var adminClient = CreateAdminClient();

        // Key A creates two links.
        var keyA = await (await adminClient.PostAsJsonAsync("/api-keys",
                new CreateApiKeyRequest("list-key-a", ["links:read", "links:write"], await _factory.SeedAccountAsync())))
            .Content.ReadFromJsonAsync<ApiKeyResponse>();
        var keyB = await (await adminClient.PostAsJsonAsync("/api-keys",
                new CreateApiKeyRequest("list-key-b", ["links:read", "links:write"], await _factory.SeedAccountAsync())))
            .Content.ReadFromJsonAsync<ApiKeyResponse>();

        var clientA = _factory.CreateClient();
        clientA.DefaultRequestHeaders.Add("Authorization", $"Bearer {keyA!.PlaintextKey}");

        var clientB = _factory.CreateClient();
        clientB.DefaultRequestHeaders.Add("Authorization", $"Bearer {keyB!.PlaintextKey}");

        await clientA.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/a1"));
        await clientA.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/a2"));
        await clientB.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com/b1"));

        var responseA = await clientA.GetAsync("/links");
        var bodyA = await responseA.Content.ReadFromJsonAsync<List<LinkResponse>>();

        Assert.Equal(2, bodyA!.Count);
        Assert.All(bodyA, l => Assert.StartsWith("https://example.com/a", l.Url));
    }

    [Fact]
    public async Task ListLinks_PaginationDefaultsToFirstPage()
    {
        var adminClient = CreateAdminClient();
        var key = await (await adminClient.PostAsJsonAsync("/api-keys",
                new CreateApiKeyRequest("paged-key", ["links:read", "links:write"], await _factory.SeedAccountAsync())))
            .Content.ReadFromJsonAsync<ApiKeyResponse>();

        var apiClient = _factory.CreateClient();
        apiClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {key!.PlaintextKey}");

        for (var i = 1; i <= 5; i++)
            await apiClient.PostAsJsonAsync("/links", new CreateLinkRequest($"https://example.com/page/{i}"));

        var response = await apiClient.GetAsync("/links?pageSize=3");
        var body = await response.Content.ReadFromJsonAsync<List<LinkResponse>>();

        Assert.Equal(3, body!.Count);
    }
}
