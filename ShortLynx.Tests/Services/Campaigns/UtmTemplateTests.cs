using ShortLynx.Services.Campaigns;

namespace ShortLynx.Tests.Services.Campaigns;

public class UtmTemplateTests
{
    [Fact]
    public void Apply_NoQuery_AppendsAllUtmParams()
    {
        var result = UtmTemplate.Apply("https://example.com/landing", "newsletter", "email", "spring");

        Assert.Contains("utm_source=newsletter", result);
        Assert.Contains("utm_medium=email", result);
        Assert.Contains("utm_campaign=spring", result);
        Assert.StartsWith("https://example.com/landing?", result);
    }

    [Fact]
    public void Apply_ExistingQuery_PreservesItAndAppends()
    {
        var result = UtmTemplate.Apply("https://example.com/p?ref=abc", "twitter", null, null);

        Assert.Contains("ref=abc", result);
        Assert.Contains("utm_source=twitter", result);
    }

    [Fact]
    public void Apply_DoesNotClobberExistingUtm()
    {
        // Destination already carries utm_source — keep it, only fill the gaps.
        var result = UtmTemplate.Apply("https://example.com/p?utm_source=manual", "auto", "email", null);

        Assert.Contains("utm_source=manual", result);
        Assert.DoesNotContain("utm_source=auto", result);
        Assert.Contains("utm_medium=email", result);
    }

    [Fact]
    public void Apply_EmptyTemplate_ReturnsUnchanged()
    {
        const string url = "https://example.com/p?x=1";
        Assert.Equal(url, UtmTemplate.Apply(url, null, null, null));
        Assert.Equal(url, UtmTemplate.Apply(url, "", "", ""));
    }

    [Fact]
    public void Apply_EncodesValues()
    {
        var result = UtmTemplate.Apply("https://example.com", "spring sale", null, null);
        Assert.Contains("utm_source=spring%20sale", result);
    }

    [Fact]
    public void Apply_PreservesFragment()
    {
        var result = UtmTemplate.Apply("https://example.com/p#section", "x", null, null);
        Assert.Contains("utm_source=x", result);
        Assert.EndsWith("#section", result);
    }

    [Theory]
    [InlineData("mailto:hello@example.com")]
    [InlineData("ftp://files.example.com/a")]
    [InlineData("not a url")]
    public void Apply_NonHttpUrl_ReturnsUnchanged(string url)
        => Assert.Equal(url, UtmTemplate.Apply(url, "src", "med", "camp"));
}
