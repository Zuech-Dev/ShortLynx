using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Admin.Components.Pages;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;
using ShortLynx.Services.ApiKeys;

namespace ShortLynx.Tests.Admin;

public class ApiKeysComponentTests : BunitContext
{
    private readonly FakeApiKeyService _keys = new();
    private readonly SqliteConnection _conn;
    private readonly Guid _uid = Guid.CreateVersion7();

    public ApiKeysComponentTests()
    {
        _conn = new SqliteConnection("DataSource=:memory:");
        _conn.Open();
        Services.AddDbContextFactory<ShortLynxDbContext>(o => o.UseSqlite(_conn));
        Services.AddScoped<IApiKeyService>(_ => _keys);

        var auth = AddAuthorization();
        auth.SetAuthorized("user@example.com");
        auth.SetClaims(new Claim(ClaimTypes.NameIdentifier, _uid.ToString()));

        JSInterop.Mode = JSRuntimeMode.Loose;
        JSInterop.Setup<bool>("confirm", _ => true).SetResult(true);

        // Create the schema so the page's LoadAsync queries succeed (resolve locks the provider).
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    private void SeedKey(Guid id, Guid uid)
    {
        var factory = Services.GetRequiredService<IDbContextFactory<ShortLynxDbContext>>();
        using var db = factory.CreateDbContext();
        db.UserAccountEntities.Add(new UserAccountEntity
        {
            Id = uid, Email = "owner@example.com", CreatedAt = DateTimeOffset.UtcNow, IsActive = true,
        });
        db.ApiKeyEntities.Add(new ApiKeyEntity
        {
            Id = id, Name = "existing", Prefix = "PREFIX12", KeyHash = "h",
            Scopes = "links:read", CreatedAt = DateTimeOffset.UtcNow, IsActive = true, UserAccountId = uid,
        });
        db.SaveChanges();
    }

    [Fact]
    public void CreateKey_Valid_ShowsPlaintextOnce_AndCallsServiceWithUser()
    {
        var cut = Render<ApiKeys>();
        cut.Find("button.btn-primary").Click();                // + New key
        cut.Find("input.form-control").Change("My Key");       // name
        cut.FindAll("input.form-check-input")[0].Change(true); // first scope
        cut.Find("form").Submit();

        Assert.Single(_keys.Created);
        Assert.Equal(_uid, _keys.Created[0].Uid);
        Assert.Contains(_keys.PlaintextToReturn, cut.Markup);
        Assert.NotNull(cut.Find("[data-testid=new-key]"));
    }

    [Fact]
    public void CreateKey_MissingName_ShowsValidation_NoServiceCall()
    {
        var cut = Render<ApiKeys>();
        cut.Find("button.btn-primary").Click();
        cut.FindAll("input.form-check-input")[0].Change(true); // scope but no name
        cut.Find("form").Submit();

        Assert.Empty(_keys.Created);
        Assert.Contains("Name is required.", cut.Markup);
    }

    [Fact]
    public void CreateKey_NoScope_ShowsScopeError_NoServiceCall()
    {
        var cut = Render<ApiKeys>();
        cut.Find("button.btn-primary").Click();
        cut.Find("input.form-control").Change("My Key");       // name but no scope
        cut.Find("form").Submit();

        Assert.Empty(_keys.Created);
        Assert.Contains("Select at least one scope.", cut.Markup);
    }

    [Fact]
    public void Revoke_Confirmed_CallsRevokeWithKeyAndUser()
    {
        var keyId = Guid.CreateVersion7();
        SeedKey(keyId, _uid);

        var cut = Render<ApiKeys>();
        cut.Find("button.btn-outline-danger").Click();         // Revoke

        Assert.Single(_keys.Revoked);
        Assert.Equal(keyId, _keys.Revoked[0].KeyId);
        Assert.Equal(_uid, _keys.Revoked[0].Uid);
    }
}
