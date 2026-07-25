using ShortLynx.Data.Enums;
using ShortLynx.Services.Links;
using ShortLynx.Tests.Infrastructure;

namespace ShortLynx.Tests.Services.Links;

/// <summary>
/// Regression cover for the 2026-07-25 production bug: every one of this method's four call sites
/// (two QR endpoints, the Admin link-detail display, and social-post composing) built a bare
/// <c>/{code}</c> URL for custom (vanity) codes, which 404s — <c>RedirectService.LookupAsync</c>
/// explicitly excludes custom codes from the root route; they resolve only under the configured
/// custom route prefix. isCustom/customRoutePrefix are now required parameters specifically so a
/// caller can't silently forget them the way all four previously did.
/// </summary>
public class ShortUrlBuilderTests
{
    [Fact]
    public async Task NonCustomCode_WithBaseUrl_BuildsPlainUrl_NoPrefix()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        ctx.AddRange(account, link);
        await ctx.SaveChangesAsync();

        var url = await ShortUrlBuilder.BuildAsync(ctx, link, "abc123", isCustom: false, customRoutePrefix: "c",
            publicBaseUrl: "https://short.ly");

        Assert.Equal("https://short.ly/abc123", url);
    }

    [Fact]
    public async Task CustomCode_WithBaseUrl_IncludesConfiguredPrefix()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        ctx.AddRange(account, link);
        await ctx.SaveChangesAsync();

        var url = await ShortUrlBuilder.BuildAsync(ctx, link, "my-vanity-code", isCustom: true, customRoutePrefix: "c",
            publicBaseUrl: "https://short.ly");

        Assert.Equal("https://short.ly/c/my-vanity-code", url);
    }

    [Fact]
    public async Task CustomCode_WithNonDefaultPrefix_UsesTheConfiguredPrefix_NotAHardcodedOne()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        ctx.AddRange(account, link);
        await ctx.SaveChangesAsync();

        var url = await ShortUrlBuilder.BuildAsync(ctx, link, "my-vanity-code", isCustom: true, customRoutePrefix: "/go/",
            publicBaseUrl: "https://short.ly");

        Assert.Equal("https://short.ly/go/my-vanity-code", url);
    }

    [Fact]
    public async Task CustomCode_WithVerifiedPinnedDomain_DomainWins_PrefixStillApplies()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();
        var account = EntityFactory.Account();
        var domain = EntityFactory.CustomDomain(account.Id);
        domain.VerificationStatus = DomainVerificationStatus.Verified;
        domain.IsActive = true;
        var link = EntityFactory.AnonymousLink(account.Id);
        link.CustomDomainId = domain.Id;
        ctx.AddRange(account, domain, link);
        await ctx.SaveChangesAsync();

        var url = await ShortUrlBuilder.BuildAsync(ctx, link, "my-vanity-code", isCustom: true, customRoutePrefix: "c",
            publicBaseUrl: "https://short.ly");

        Assert.Equal($"https://{domain.Domain}/c/my-vanity-code", url);
    }

    [Fact]
    public async Task NonCustomCode_WithVerifiedPinnedDomain_DomainWins_NoPrefix()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();
        var account = EntityFactory.Account();
        var domain = EntityFactory.CustomDomain(account.Id);
        domain.VerificationStatus = DomainVerificationStatus.Verified;
        domain.IsActive = true;
        var link = EntityFactory.AnonymousLink(account.Id);
        link.CustomDomainId = domain.Id;
        ctx.AddRange(account, domain, link);
        await ctx.SaveChangesAsync();

        var url = await ShortUrlBuilder.BuildAsync(ctx, link, "abc123", isCustom: false, customRoutePrefix: "c",
            publicBaseUrl: "https://short.ly");

        Assert.Equal($"https://{domain.Domain}/abc123", url);
    }

    [Fact]
    public async Task NoBaseUrlConfigured_NonCustom_ReturnsBareCode()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        ctx.AddRange(account, link);
        await ctx.SaveChangesAsync();

        var url = await ShortUrlBuilder.BuildAsync(ctx, link, "abc123", isCustom: false, customRoutePrefix: "c",
            publicBaseUrl: null);

        Assert.Equal("abc123", url);
    }

    [Fact]
    public async Task NoBaseUrlConfigured_Custom_StillGetsPrefix()
    {
        await using var db = await TestDatabase.CreateAsync();
        await using var ctx = db.CreateContext();
        var account = EntityFactory.Account();
        var link = EntityFactory.AnonymousLink(account.Id);
        ctx.AddRange(account, link);
        await ctx.SaveChangesAsync();

        var url = await ShortUrlBuilder.BuildAsync(ctx, link, "my-vanity-code", isCustom: true, customRoutePrefix: "c",
            publicBaseUrl: null);

        Assert.Equal("c/my-vanity-code", url);
    }
}
