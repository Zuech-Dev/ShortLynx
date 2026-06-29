using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Admin.Components.Layout;
using ShortLynx.Admin.Options;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Accounts;

namespace ShortLynx.Tests.Admin;

public class AccountSwitcherComponentTests : BunitContext
{
    private readonly FakeAccountService _accounts = new();
    private readonly Guid _uid = Guid.CreateVersion7();
    private readonly Guid _accountA = Guid.CreateVersion7();
    private readonly Guid _accountB = Guid.CreateVersion7();

    public AccountSwitcherComponentTests()
    {
        Services.AddScoped<IAccountService>(_ => _accounts);
        JSInterop.Mode = JSRuntimeMode.Loose;

        var auth = AddAuthorization();
        auth.SetAuthorized("user@example.com");
        auth.SetClaims(
            new Claim(ClaimTypes.NameIdentifier, _uid.ToString()),
            new Claim(AdminClaims.AccountId, _accountA.ToString()));
    }

    [Fact]
    public void Renders_Switcher_With_All_Accounts_When_User_Has_Multiple()
    {
        _accounts.AccountsFor = _ =>
        [
            new AccountSummary(_accountA, "Acme", AccountRole.Owner),
            new AccountSummary(_accountB, "Globex", AccountRole.Member),
        ];

        var cut = Render<AccountSwitcher>();

        Assert.Contains("account-switcher", cut.Markup);
        Assert.Equal(2, cut.FindAll("[data-testid=account-select] option").Count);
        Assert.Contains("Acme", cut.Markup);
        Assert.Contains("Globex", cut.Markup);
        // Posts to the switch endpoint.
        Assert.Equal("/auth/switch", cut.Find("form").GetAttribute("action"));
    }

    [Fact]
    public void Hidden_When_User_Has_Single_Account()
    {
        _accounts.AccountsFor = _ => [new AccountSummary(_accountA, "Acme", AccountRole.Owner)];

        var cut = Render<AccountSwitcher>();

        Assert.DoesNotContain("account-switcher", cut.Markup);
    }
}
