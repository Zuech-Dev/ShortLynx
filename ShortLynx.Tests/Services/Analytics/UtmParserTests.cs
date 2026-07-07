using ShortLynx.Services.Analytics;

namespace ShortLynx.Tests.Services.Analytics;

public class UtmParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("?")]
    [InlineData("?foo=bar&baz=1")]
    public void Parse_NoUtmParams_ReturnsEmpty(string? query)
        => Assert.Equal(UtmTags.Empty, UtmParser.Parse(query));

    [Fact]
    public void Parse_AllFiveTags()
    {
        var t = UtmParser.Parse("?utm_source=newsletter&utm_medium=email&utm_campaign=launch&utm_term=links&utm_content=cta");

        Assert.Equal(new UtmTags("newsletter", "email", "launch", "links", "cta"), t);
    }

    [Fact]
    public void Parse_IsCaseInsensitiveOnKeys_AndIgnoresNonUtmParams()
    {
        var t = UtmParser.Parse("UTM_Source=x&ref=ignored&utm_CAMPAIGN=y");

        Assert.Equal("x", t.Source);
        Assert.Equal("y", t.Campaign);
        Assert.Null(t.Medium);
    }

    [Fact]
    public void Parse_UrlDecodesValues_IncludingPlusAsSpace()
    {
        var t = UtmParser.Parse("?utm_campaign=spring%20launch&utm_source=my+list");

        Assert.Equal("spring launch", t.Campaign);
        Assert.Equal("my list", t.Source);
    }

    [Fact]
    public void Parse_FirstOccurrenceWins()
        => Assert.Equal("a", UtmParser.Parse("?utm_source=a&utm_source=b").Source);

    [Fact]
    public void Parse_TruncatesOversizedValues()
    {
        var t = UtmParser.Parse($"?utm_source={new string('x', 500)}");

        Assert.Equal(100, t.Source!.Length);
    }

    [Theory]
    [InlineData("?utm_source=")] // empty value
    [InlineData("?utm_source")]  // no '='
    public void Parse_DropsUnusableValues(string query)
        => Assert.Null(UtmParser.Parse(query).Source);

    [Fact]
    public void Parse_MalformedEscaping_PassesThroughAsLiteral()
        // Uri.UnescapeDataString leaves invalid sequences untouched rather than throwing —
        // the value is stored as-is (harmless, bounded by the length cap).
        => Assert.Equal("%ZZ", UtmParser.Parse("?utm_source=%ZZ").Source);
}
