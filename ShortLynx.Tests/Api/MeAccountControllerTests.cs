using System.Net;
using System.Net.Http.Json;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Enums;

namespace ShortLynx.Tests.Api;

public class MeAccountControllerTests : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory;
    public MeAccountControllerTests(ApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Get_ReturnsCurrentAccountSettings()
    {
        var (client, _, accountId) = await _factory.CreateSessionClientAsync();

        var settings = await client.GetFromJsonAsync<AccountSettingsResponse>("/me/account");

        Assert.Equal(accountId, settings!.Id);
    }

    [Fact]
    public async Task Update_AsOwner_RenamesAccount()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        var newName = $"Renamed {Guid.NewGuid():N}";

        var resp = await client.PutAsJsonAsync("/me/account", new UpdateAccountRequest(newName, null, null));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var settings = await resp.Content.ReadFromJsonAsync<AccountSettingsResponse>();
        Assert.Equal(newName, settings!.Name);
        Assert.Null(settings.PrivacyPolicyUrl);
        Assert.Null(settings.TermsOfServiceUrl);
    }

    [Fact]
    public async Task Update_SetsPrivacyAndTermsUrls_WhenConfirmed()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var resp = await client.PutAsJsonAsync("/me/account", new UpdateAccountRequest(
            "Acme", "https://acme.example/privacy", "https://acme.example/terms", ConfirmsDisclosure: true));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var settings = await resp.Content.ReadFromJsonAsync<AccountSettingsResponse>();
        Assert.Equal("https://acme.example/privacy", settings!.PrivacyPolicyUrl);
        Assert.Equal("https://acme.example/terms", settings.TermsOfServiceUrl);
    }

    [Fact]
    public async Task Update_PrivacyUrlWithoutConfirmation_Returns400()
    {
        // Matches Admin's own Settings.razor: a policy URL turns off the recipient-facing disclosure
        // interstitial, so it's never accepted without an explicit "I confirm this discloses tracking".
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var resp = await client.PutAsJsonAsync("/me/account", new UpdateAccountRequest(
            "Acme", "https://acme.example/privacy", null, ConfirmsDisclosure: false));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_PlainHttpPrivacyUrl_Returns400()
    {
        // Admin's Settings.razor requires https:// specifically, not just any http(s) scheme.
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var resp = await client.PutAsJsonAsync("/me/account", new UpdateAccountRequest(
            "Acme", "http://acme.example/privacy", null, ConfirmsDisclosure: true));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_BlankPrivacyUrl_ClearsAPreviouslySetValue()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();
        await client.PutAsJsonAsync("/me/account",
            new UpdateAccountRequest("Acme", "https://acme.example/privacy", null, ConfirmsDisclosure: true));

        // Clearing the field back to blank doesn't need confirmation -- only setting a real value does.
        var resp = await client.PutAsJsonAsync("/me/account", new UpdateAccountRequest("Acme", "", null));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var settings = await resp.Content.ReadFromJsonAsync<AccountSettingsResponse>();
        Assert.Null(settings!.PrivacyPolicyUrl);
    }

    [Fact]
    public async Task Update_MalformedPrivacyUrl_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var resp = await client.PutAsJsonAsync("/me/account",
            new UpdateAccountRequest("Acme", "not-a-url", null, ConfirmsDisclosure: true));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_BlankName_Returns400()
    {
        var (client, _, _) = await _factory.CreateSessionClientAsync();

        var resp = await client.PutAsJsonAsync("/me/account", new UpdateAccountRequest("", null, null));

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_AsMember_Returns403()
    {
        // Rename/settings changes are ManageAccount (Owner-only) -- a Member may read but not write.
        var (client, _, _) = await _factory.CreateSessionClientAsync(AccountRole.Member);

        var resp = await client.PutAsJsonAsync("/me/account", new UpdateAccountRequest("New Name", null, null));

        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    [Fact]
    public async Task Get_RequiresSession()
    {
        var resp = await _factory.CreateClient().GetAsync("/me/account");
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }
}
