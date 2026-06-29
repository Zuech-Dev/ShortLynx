using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.ApiKeys;

namespace ShortLynx.Tests.Api;

public class DomainsControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;

    public DomainsControllerTests(ApiFactory factory) => _factory = factory;

    // A unique domain per test — the DB is shared across the class and Domain has a global unique index.
    private static string UniqueDomain() => $"d{Guid.NewGuid():N}.example.com";

    // Creates an API key associated with a fresh user account (custom domains are user-scoped).
    private async Task<(HttpClient Client, Guid UserId)> CreateUserKeyClientAsync(string[]? scopes = null)
    {
        var accountId = await _factory.SeedAccountAsync();
        using var scope = _factory.Services.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IApiKeyService>();
        var granted = scopes ?? [Scopes.DomainsRead, Scopes.DomainsWrite, Scopes.LinksRead, Scopes.LinksWrite];
        var (_, plaintext) = await svc.CreateAsync("dom-key", granted, accountId);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", $"Bearer {plaintext}");
        return (client, accountId);
    }

    private static async Task<DomainResponse> AddDomainAsync(HttpClient client, string domain)
    {
        var response = await client.PostAsJsonAsync("/domains", new AddDomainRequest(domain));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DomainResponse>();
        Assert.NotNull(body);
        return body;
    }

    // ── Auth & scoping ──────────────────────────────────────────────────────

    [Fact]
    public async Task AddDomain_NoAuth_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/domains", new AddDomainRequest(UniqueDomain()));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AddDomain_WithoutDomainsWriteScope_Returns403()
    {
        var (client, _) = await CreateUserKeyClientAsync([Scopes.DomainsRead]);
        var response = await client.PostAsJsonAsync("/domains", new AddDomainRequest(UniqueDomain()));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── CRUD + verification ─────────────────────────────────────────────────

    [Fact]
    public async Task AddDomain_Returns201_PendingWithTxtInstructions()
    {
        var (client, _) = await CreateUserKeyClientAsync();
        var domain = UniqueDomain();

        var body = await AddDomainAsync(client, domain);

        Assert.Equal(domain, body.Domain);
        Assert.Equal("Pending", body.Status);
        Assert.Null(body.VerifiedAt);
        Assert.Equal($"_shortlynx-verify.{domain}", body.VerificationHost);
        Assert.StartsWith("shortlynx-verify=", body.VerificationTxtValue);
    }

    [Fact]
    public async Task AddDomain_Duplicate_Returns409()
    {
        var (client, _) = await CreateUserKeyClientAsync();
        var domain = UniqueDomain();
        await AddDomainAsync(client, domain);

        var second = await client.PostAsJsonAsync("/domains", new AddDomainRequest(domain));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task List_ReturnsOwnDomains()
    {
        var (client, _) = await CreateUserKeyClientAsync();
        var domain = UniqueDomain();
        await AddDomainAsync(client, domain);

        var list = await client.GetFromJsonAsync<List<DomainResponse>>("/domains");
        Assert.NotNull(list);
        Assert.Contains(list, d => d.Domain == domain);
    }

    [Fact]
    public async Task Verify_WithMatchingTxtRecord_Returns200Verified()
    {
        var (client, _) = await CreateUserKeyClientAsync();
        var added = await AddDomainAsync(client, UniqueDomain());

        // Publish exactly the TXT the API told us to create.
        _factory.Dns.Publish(added.VerificationHost, added.VerificationTxtValue);

        var response = await client.PostAsync($"/domains/{added.Id}/verify", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var verified = await response.Content.ReadFromJsonAsync<DomainResponse>();
        Assert.NotNull(verified);
        Assert.Equal("Verified", verified.Status);
        Assert.NotNull(verified.VerifiedAt);
    }

    [Fact]
    public async Task Verify_WithoutMatchingRecord_Returns200Failed()
    {
        var (client, _) = await CreateUserKeyClientAsync();
        var added = await AddDomainAsync(client, UniqueDomain());

        var response = await client.PostAsync($"/domains/{added.Id}/verify", null);
        var verified = await response.Content.ReadFromJsonAsync<DomainResponse>();
        Assert.NotNull(verified);
        Assert.Equal("Failed", verified.Status);
    }

    [Fact]
    public async Task Delete_RemovesDomain_Returns204()
    {
        var (client, _) = await CreateUserKeyClientAsync();
        var added = await AddDomainAsync(client, UniqueDomain());

        var del = await client.DeleteAsync($"/domains/{added.Id}");
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var get = await client.GetAsync($"/domains/{added.Id}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ── Link pinning ────────────────────────────────────────────────────────

    [Fact]
    public async Task SetLinkDomain_VerifiedDomain_Returns204()
    {
        var (client, _) = await CreateUserKeyClientAsync();

        // Create a link owned by this key.
        var linkResp = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com"));
        var link = await linkResp.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(link);

        // Add + verify a domain.
        var added = await AddDomainAsync(client, UniqueDomain());
        _factory.Dns.Publish(added.VerificationHost, added.VerificationTxtValue);
        await client.PostAsync($"/domains/{added.Id}/verify", null);

        var pin = await client.PutAsJsonAsync($"/links/{link.Id}/domain", new SetLinkDomainRequest(added.Id));
        Assert.Equal(HttpStatusCode.NoContent, pin.StatusCode);

        // Unpin works too.
        var unpin = await client.PutAsJsonAsync($"/links/{link.Id}/domain", new SetLinkDomainRequest(null));
        Assert.Equal(HttpStatusCode.NoContent, unpin.StatusCode);
    }

    [Fact]
    public async Task SetLinkDomain_UnverifiedDomain_Returns400()
    {
        var (client, _) = await CreateUserKeyClientAsync();

        var linkResp = await client.PostAsJsonAsync("/links", new CreateLinkRequest("https://example.com"));
        var link = await linkResp.Content.ReadFromJsonAsync<LinkResponse>();
        Assert.NotNull(link);

        // Pending (unverified) domain.
        var added = await AddDomainAsync(client, UniqueDomain());

        var pin = await client.PutAsJsonAsync($"/links/{link.Id}/domain", new SetLinkDomainRequest(added.Id));
        Assert.Equal(HttpStatusCode.BadRequest, pin.StatusCode);
    }
}
