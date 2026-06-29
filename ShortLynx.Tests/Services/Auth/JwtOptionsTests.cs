using ShortLynx.Services.Auth;

namespace ShortLynx.Tests.Services.Auth;

public class JwtOptionsTests
{
    private static JwtOptions Valid() => new() { SigningKey = new string('k', 40) };

    [Fact]
    public void ValidKey_IsValid()
        => Assert.True(Valid().IsValid);

    [Fact]
    public void EmptyKey_IsInvalid()
        => Assert.False(new JwtOptions { SigningKey = "" }.IsValid);

    [Fact]
    public void PlaceholderKey_IsInvalid()
        => Assert.False(new JwtOptions { SigningKey = JwtOptions.DefaultPlaceholderKey }.IsValid);

    [Fact]
    public void ShortKey_IsInvalid()
        => Assert.False(new JwtOptions { SigningKey = "too-short" }.IsValid);

    [Theory]
    [InlineData(0, 30)]
    [InlineData(15, 0)]
    [InlineData(-1, 30)]
    public void NonPositiveLifetimes_AreInvalid(int accessMinutes, int refreshDays)
    {
        var opts = Valid();
        opts.AccessTokenMinutes = accessMinutes;
        opts.RefreshTokenDays = refreshDays;
        Assert.False(opts.IsValid);
    }

    [Fact]
    public void Lifetimes_ComputeFromConfiguredValues()
    {
        var opts = new JwtOptions { SigningKey = new string('k', 40), AccessTokenMinutes = 10, RefreshTokenDays = 7 };
        Assert.Equal(TimeSpan.FromMinutes(10), opts.AccessTokenLifetime);
        Assert.Equal(TimeSpan.FromDays(7), opts.RefreshTokenLifetime);
    }
}
