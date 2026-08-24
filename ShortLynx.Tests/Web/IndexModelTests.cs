using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShortLynx.Services.Redirect;
using ShortLynx.Web.Pages;

namespace ShortLynx.Tests.Web;

public class IndexModelTests
{
    private static IndexModel MakeModel(string? marketingRedirectUrl)
    {
        var options = Options.Create(new RedirectOptions { MarketingRedirectUrl = marketingRedirectUrl });
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("shrtlynx.com");

        return new IndexModel(options) { PageContext = new PageContext { HttpContext = http } };
    }

    [Fact]
    public void OnGet_NoMarketingRedirectUrl_RendersPageAndSetsCanonical()
    {
        var model = MakeModel(marketingRedirectUrl: null);

        var result = model.OnGet();

        Assert.IsType<PageResult>(result);
        Assert.Equal("https://shrtlynx.com/", model.Canonical);
    }

    [Fact]
    public void OnGet_MarketingRedirectUrlSet_RedirectsPermanently()
    {
        var model = MakeModel(marketingRedirectUrl: "https://shortlynx.dev");

        var result = model.OnGet();

        var redirect = Assert.IsType<RedirectResult>(result);
        Assert.Equal("https://shortlynx.dev", redirect.Url);
        Assert.True(redirect.Permanent);
    }

    [Fact]
    public void OnGet_BlankMarketingRedirectUrl_RendersPageInstead()
    {
        // A blank string (as opposed to null) is a plausible misconfiguration -- an operator clearing
        // an env var to "" rather than unsetting it. Must fall back to the normal page, not redirect
        // to an empty URL.
        var model = MakeModel(marketingRedirectUrl: "   ");

        var result = model.OnGet();

        Assert.IsType<PageResult>(result);
    }
}
