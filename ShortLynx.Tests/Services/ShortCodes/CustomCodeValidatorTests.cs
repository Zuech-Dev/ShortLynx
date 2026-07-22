using Microsoft.Extensions.Options;
using ShortLynx.Services.ShortCodes;

namespace ShortLynx.Tests.Services.ShortCodes;

public class CustomCodeValidatorTests
{
    private static CustomCodeValidator Validator(ShortCodeOptions? opts = null)
        => new(Options.Create(opts ?? new ShortCodeOptions()));

    [Theory]
    [InlineData("abcdefgh")]        // 8 = min
    [InlineData("voter2025")]       // digits, no hyphen
    [InlineData("go-vote-25")]      // multiple internal hyphens
    [InlineData("my-link1")]        // 8 incl. hyphen
    [InlineData("abcdefghijkl")]    // 12 = default max
    public void Accepts_ValidCodes(string code)
        => Assert.True(Validator().Validate(code).IsValid, code);

    [Fact]
    public void UppercaseInput_IsNormalized_NotRejected()
    {
        var r = Validator().Validate("Go-Vote-25");
        Assert.True(r.IsValid);
        Assert.Equal("go-vote-25", CustomCodeValidator.Normalize("Go-Vote-25"));
    }

    [Theory]
    [InlineData("short", "at least")]           // 5 chars
    [InlineData("abcdefghijklm", "at most")]    // 13 chars > max 12
    public void Rejects_OutOfLength(string code, string reasonContains)
    {
        var r = Validator().Validate(code);
        Assert.False(r.IsValid);
        Assert.Contains(reasonContains, r.Reason);
    }

    [Theory]
    [InlineData("my_link12")]   // underscore
    [InlineData("my link12")]   // space
    [InlineData("my.link12")]   // dot
    [InlineData("my--link1")]   // consecutive hyphen
    [InlineData("-mylink12")]   // leading hyphen
    [InlineData("mylink12-")]   // trailing hyphen
    public void Rejects_BadCharset(string code)
    {
        var r = Validator().Validate(code);
        Assert.False(r.IsValid);
        Assert.Contains("lowercase", r.Reason);
    }

    [Theory]
    [InlineData("disclosure")]   // reserved system route
    [InlineData("dashboard")]    // impersonation term (single segment)
    [InlineData("settings")]     // impersonation term, 8 chars
    [InlineData("my-admin-x")]   // impersonation term as a segment
    public void Rejects_ReservedAndImpersonation(string code)
    {
        var r = Validator().Validate(code);
        Assert.False(r.IsValid);
        Assert.Contains("reserved", r.Reason);
    }

    [Fact]
    public void Rejects_Profanity_AsSubstring()
    {
        var r = Validator().Validate("myshitcode");
        Assert.False(r.IsValid);
        Assert.Contains("isn't allowed", r.Reason);
    }

    [Fact]
    public void EmptyOrWhitespace_IsRejected()
    {
        Assert.False(Validator().Validate(null).IsValid);
        Assert.False(Validator().Validate("   ").IsValid);
    }

    [Fact]
    public void MaxLength_IsConfigurable()
    {
        var opts = new ShortCodeOptions { CustomCodeMaxLength = 16 };
        Assert.True(Validator(opts).Validate("abcdefghijklmnop").IsValid);   // 16
        Assert.False(Validator(opts).Validate("abcdefghijklmnopq").IsValid); // 17
    }

    [Fact]
    public void CustomPrefixSegment_IsReserved()
    {
        // A code equal to the configured route prefix is reserved (avoids /c/c ambiguity).
        var opts = new ShortCodeOptions { CustomRoutePrefix = "golinks" };
        Assert.False(Validator(opts).Validate("golinks").IsValid);
    }
}
