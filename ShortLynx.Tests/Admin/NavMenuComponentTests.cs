using Bunit;
using Bunit.TestDoubles;
using ShortLynx.Admin.Components.Layout;
using ShortLynx.Admin.Options;
using ShortLynx.Admin.Services;

namespace ShortLynx.Tests.Admin;

public class NavMenuComponentTests : BunitContext
{
    private static readonly string[] AlwaysVisibleLabels =
        ["Dashboard", "Links", "API Keys", "Campaigns", "Social", "Domains", "Members", "Settings"];

    public NavMenuComponentTests() => JSInterop.Mode = JSRuntimeMode.Loose;

    [Fact]
    public void Hamburger_NonAdmin_StartsClosed_ExpandsOnClick_NoUsersLink()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("member@example.com");

        var cut = Render<NavMenu>(p => p.Add(x => x.Style, NavStyle.Hamburger));

        var toggle = cut.Find("button[aria-controls=primary-nav]");
        Assert.Equal("false", toggle.GetAttribute("aria-expanded"));
        Assert.Contains("hidden", cut.Find("#primary-nav").GetAttribute("class")!.Split(' '));

        foreach (var label in AlwaysVisibleLabels) Assert.Contains(label, cut.Markup);
        Assert.DoesNotContain("Users", cut.Markup);

        toggle.Click();

        toggle = cut.Find("button[aria-controls=primary-nav]");
        Assert.Equal("true", toggle.GetAttribute("aria-expanded"));
        var navClasses = cut.Find("#primary-nav").GetAttribute("class")!.Split(' ');
        Assert.Contains("flex", navClasses);
        Assert.DoesNotContain("hidden", navClasses);
    }

    [Fact]
    public void Hamburger_SuperAdmin_ShowsUsersLink()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("admin@example.com");
        auth.SetPolicies(AdminClaims.SuperAdminPolicy);

        var cut = Render<NavMenu>(p => p.Add(x => x.Style, NavStyle.Hamburger));

        Assert.Contains("Users", cut.Markup);
    }

    [Fact]
    public void HorizontalScroll_RendersAllLinks_WithNoToggleButton()
    {
        var auth = AddAuthorization();
        auth.SetAuthorized("member@example.com");

        var cut = Render<NavMenu>(p => p.Add(x => x.Style, NavStyle.HorizontalScroll));

        Assert.Empty(cut.FindAll("button[aria-controls=primary-nav]"));
        Assert.Contains("overflow-x-auto", cut.Find("#primary-nav").GetAttribute("class")!.Split(' '));
        foreach (var label in AlwaysVisibleLabels) Assert.Contains(label, cut.Markup);
        Assert.DoesNotContain("Users", cut.Markup); // same policy gate applies regardless of style
    }
}
