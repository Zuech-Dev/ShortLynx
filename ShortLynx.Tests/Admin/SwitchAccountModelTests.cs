using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ShortLynx.Admin.Options;
using ShortLynx.Admin.Pages.Auth;
using ShortLynx.Data.Enums;

namespace ShortLynx.Tests.Admin;

public class SwitchAccountModelTests
{
    private readonly Guid _uid = Guid.CreateVersion7();

    private (SwitchAccountModel Model, DefaultHttpContext Http) MakeModel(FakeAccountService accounts)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDataProtection();
        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie();
        services.AddAuthorization();
        var sp = services.BuildServiceProvider();

        // Signed-in user currently scoped to some "old" account.
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, _uid.ToString()),
                new Claim(ClaimTypes.Name, "user@example.com"),
                new Claim(AdminClaims.AccountId, Guid.CreateVersion7().ToString()),
                new Claim(AdminClaims.AccountRole, AccountRole.Owner.ToString()),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);

        var http = new DefaultHttpContext
        {
            RequestServices = sp,
            User = new ClaimsPrincipal(identity),
        };
        var model = new SwitchAccountModel(accounts)
        {
            PageContext = new PageContext { HttpContext = http },
            Url = new UrlHelperStub(),
        };
        return (model, http);
    }

    // The model only needs Url.IsLocalUrl; a tiny stub avoids standing up MVC routing.
    private sealed class UrlHelperStub : IUrlHelper
    {
        public Microsoft.AspNetCore.Mvc.ActionContext ActionContext { get; } = new();
        public bool IsLocalUrl(string? url) => !string.IsNullOrEmpty(url) && url.StartsWith('/') && !url.StartsWith("//");
        public string? Action(UrlActionContext actionContext) => null;
        public string? Content(string? contentPath) => contentPath;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }

    [Fact]
    public async Task Member_Selection_ReIssues_Cookie_And_Redirects()
    {
        var accountId = Guid.CreateVersion7();
        var accounts = new FakeAccountService { MembershipExists = true, Role = AccountRole.Member };
        var (model, http) = MakeModel(accounts);

        var result = await model.OnPostAsync(accountId, "/links", default);

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/links", redirect.Url);
        // A fresh auth cookie was written (the switch took effect).
        Assert.True(http.Response.Headers.SetCookie.Count > 0);
    }

    [Fact]
    public async Task NonMember_Selection_Does_Not_Switch()
    {
        var accountId = Guid.CreateVersion7();
        var accounts = new FakeAccountService { MembershipExists = false };
        var (model, http) = MakeModel(accounts);

        var result = await model.OnPostAsync(accountId, "/links", default);

        // Redirected back, but no new cookie issued — the account scope is unchanged.
        Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal(0, http.Response.Headers.SetCookie.Count);
    }

    [Fact]
    public async Task NonLocal_ReturnUrl_Is_Ignored()
    {
        var accountId = Guid.CreateVersion7();
        var accounts = new FakeAccountService { MembershipExists = true, Role = AccountRole.Owner };
        var (model, _) = MakeModel(accounts);

        var result = await model.OnPostAsync(accountId, "https://evil.example.com", default);

        var redirect = Assert.IsType<LocalRedirectResult>(result);
        Assert.Equal("/", redirect.Url);
    }
}
