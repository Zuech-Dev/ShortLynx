using System.Net;
using System.Net.Http.Json;
using ShortLynx.Core.Models.Requests;

namespace ShortLynx.Tests.Api;

public class CsrfTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public CsrfTests(ApiFactory factory) => _factory = factory;

    // Logs in and returns the (access, csrf) cookie values from the Set-Cookie headers.
    private async Task<(string Access, string Csrf)> LoginAndReadCookiesAsync()
    {
        var (token, _, _) = await _factory.SeedMemberTokenAsync();
        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/auth/session", new CreateSessionRequest(token));

        var cookies = response.Headers.GetValues("Set-Cookie")
            .Select(c => c.Split(';', 2)[0])
            .Select(kv => kv.Split('=', 2))
            .ToDictionary(p => p[0], p => p[1]);
        return (cookies["sl_access"], cookies["sl_csrf"]);
    }

    [Fact]
    public async Task CookieAuth_UnsafeMethod_WithoutCsrfHeader_Returns403()
    {
        var (access, csrf) = await LoginAndReadCookiesAsync();
        var client = _factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/me/links")
        {
            Content = JsonContent.Create(new CreateMyLinkRequest("https://example.com")),
        };
        req.Headers.Add("Cookie", $"sl_access={access}; sl_csrf={csrf}");
        // No X-CSRF-Token header.

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CookieAuth_UnsafeMethod_WithMatchingCsrfHeader_Succeeds()
    {
        var (access, csrf) = await LoginAndReadCookiesAsync();
        var client = _factory.CreateClient();

        var req = new HttpRequestMessage(HttpMethod.Post, "/me/links")
        {
            Content = JsonContent.Create(new CreateMyLinkRequest("https://example.com")),
        };
        req.Headers.Add("Cookie", $"sl_access={access}; sl_csrf={csrf}");
        req.Headers.Add("X-CSRF-Token", csrf);

        var response = await client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task BearerHeader_IsExemptFromCsrf()
    {
        // The Bearer-header session client creates links without any CSRF token.
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var response = await client.PostAsJsonAsync("/me/links", new CreateMyLinkRequest("https://example.com"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
