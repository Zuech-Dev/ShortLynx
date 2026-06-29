using Microsoft.Extensions.Options;
using ShortLynx.Services.ShortCodes;

namespace ShortLynx.Tests.Services.ShortCodes;

public class RandomBase62GeneratorTests
{
    private static RandomBase62Generator Make(int length = 8)
        => new(Options.Create(new ShortCodeOptions { Length = length }));

    private const string ValidChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    [Fact]
    public void Generate_ReturnsCodeOfConfiguredLength()
    {
        var code = Make(8).Generate(Guid.Empty);
        Assert.Equal(8, code.Length);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(12)]
    public void Generate_HonorsLengthOption(int length)
    {
        var code = Make(length).Generate(Guid.Empty);
        Assert.Equal(length, code.Length);
    }

    [Fact]
    public void Generate_ContainsOnlyBase62Characters()
    {
        var gen = Make(32);
        for (var i = 0; i < 50; i++)
        {
            var code = gen.Generate(Guid.NewGuid());
            Assert.All(code, c => Assert.Contains(c, ValidChars));
        }
    }

    [Fact]
    public void Generate_ProducesDifferentCodesOnSuccessiveCalls()
    {
        // Two independent calls must produce different codes with overwhelming probability.
        var gen = Make(8);
        var codes = Enumerable.Range(0, 20).Select(_ => gen.Generate(Guid.NewGuid())).ToHashSet();
        Assert.True(codes.Count > 1, "Expected multiple distinct codes from random generator.");
    }

    [Fact]
    public void Generate_IgnoresLinkIdUserIdAndAttempt()
    {
        // All these calls should still return *valid* Base62 codes of the right length —
        // the generator doesn't use the arguments but must handle any input.
        var gen = Make(8);
        var linkId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var a = gen.Generate(linkId);
        var b = gen.Generate(linkId, userId);
        var c = gen.Generate(linkId, userId, attempt: 5);

        Assert.Equal(8, a.Length);
        Assert.Equal(8, b.Length);
        Assert.Equal(8, c.Length);
    }
}
