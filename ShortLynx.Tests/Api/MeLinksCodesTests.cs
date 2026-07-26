using System.Net;
using System.Net.Http.Json;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;

namespace ShortLynx.Tests.Api;

/// <summary>
/// POST /me/links/{id}/codes — provisioning per-recipient (Mode 2) codes from the dashboard. The
/// labelled-recipient shape (vs. the older bare-userIds one) is what lets a dashboard show
/// "recipient → short URL" instead of an opaque list of codes; see CreateUserCodesRequest.
/// </summary>
public class MeLinksCodesTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeLinksCodesTests(ApiFactory factory) => _factory = factory;

    private static async Task<Guid> CreateUserAttributedLinkAsync(HttpClient client)
    {
        var link = await (await client.PostAsJsonAsync("/me/links",
                new CreateMyLinkRequest("https://example.com/b", "UserAttributed")))
            .Content.ReadFromJsonAsync<LinkResponse>();
        return link!.Id;
    }

    [Fact]
    public async Task WithRecipients_StampsLabelAndOneTimeFlag()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var linkId = await CreateUserAttributedLinkAsync(client);

        var recipients = new[] { new CodeRecipientRequest(Guid.CreateVersion7(), "alice@example.com") };
        var resp = await client.PostAsJsonAsync($"/me/links/{linkId}/codes",
            new CreateUserCodesRequest(Recipients: recipients, IsOneTimeUse: true));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var code = (await resp.Content.ReadFromJsonAsync<List<UserCodeResponse>>())!.Single();
        Assert.Equal("alice@example.com", code.Recipient);
        Assert.True(code.IsOneTimeUse);
    }

    [Fact]
    public async Task WithBareUserIds_BackCompat_NoLabelNeverOneTime()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var linkId = await CreateUserAttributedLinkAsync(client);

        var resp = await client.PostAsJsonAsync($"/me/links/{linkId}/codes",
            new CreateUserCodesRequest([Guid.CreateVersion7()]));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var code = (await resp.Content.ReadFromJsonAsync<List<UserCodeResponse>>())!.Single();
        Assert.Null(code.Recipient);
        Assert.False(code.IsOneTimeUse);
    }

    [Fact]
    public async Task NeitherUserIdsNorRecipients_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var linkId = await CreateUserAttributedLinkAsync(client);

        var resp = await client.PostAsJsonAsync($"/me/links/{linkId}/codes", new CreateUserCodesRequest());

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task ProvisionedRecipientLabel_ShowsUpOnAnalytics()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var linkId = await CreateUserAttributedLinkAsync(client);

        await client.PostAsJsonAsync($"/me/links/{linkId}/codes",
            new CreateUserCodesRequest(Recipients: [new CodeRecipientRequest(Guid.CreateVersion7(), "bob@example.com")]));

        var analytics = await (await client.GetAsync($"/me/links/{linkId}/analytics"))
            .Content.ReadFromJsonAsync<LinkAnalyticsResponse>();

        var stat = analytics!.Codes.Single();
        Assert.Equal("bob@example.com", stat.Recipient);
    }

    [Fact]
    public async Task LinkOfAnotherAccount_Returns404()
    {
        var (ownerClient, _, _) = await _factory.CreateSessionClientAsync();
        var linkId = await CreateUserAttributedLinkAsync(ownerClient);

        var (otherClient, _, _) = await _factory.CreateSessionClientAsync();
        var resp = await otherClient.PostAsJsonAsync($"/me/links/{linkId}/codes",
            new CreateUserCodesRequest(Recipients: [new CodeRecipientRequest(Guid.CreateVersion7())]));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
