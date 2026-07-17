using Microsoft.Extensions.Options;
using ShortLynx.Services.ShortCodes;

namespace ShortLynx.Tests.Services.ShortCodes;

public class HashBase62GeneratorTests
{
    private static HashBase62Generator Make(int length = 8)
        => new(Options.Create(new ShortCodeOptions { Length = length }));

    private const string ValidChars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    [Fact]
    public void Generate_ReturnsCodeOfConfiguredLength()
    {
        var code = Make(8).Generate(Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.Equal(8, code.Length);
    }

    [Fact]
    public void Generate_ContainsOnlyBase62Characters()
    {
        var gen = Make(8);
        var code = gen.Generate(Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.All(code, c => Assert.Contains(c, ValidChars));
    }

    [Fact]
    public void Generate_IsDeterministic_SameInputsSameOutput()
    {
        var gen = Make(8);
        var linkId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();

        var first = gen.Generate(linkId, userId, attempt: 0);
        var second = gen.Generate(linkId, userId, attempt: 0);

        Assert.Equal(first, second);
    }

    [Fact]
    public void Generate_DifferentLinkIds_ProduceDifferentCodes()
    {
        var gen = Make(8);
        var userId = Guid.CreateVersion7();
        var code1 = gen.Generate(Guid.CreateVersion7(), userId);
        var code2 = gen.Generate(Guid.CreateVersion7(), userId);
        Assert.NotEqual(code1, code2);
    }

    [Fact]
    public void Generate_DifferentUserIds_ProduceDifferentCodes()
    {
        var gen = Make(8);
        var linkId = Guid.CreateVersion7();
        var code1 = gen.Generate(linkId, Guid.CreateVersion7());
        var code2 = gen.Generate(linkId, Guid.CreateVersion7());
        Assert.NotEqual(code1, code2);
    }

    [Fact]
    public void Generate_DifferentAttempts_ProduceDifferentCodes()
    {
        var gen = Make(8);
        var linkId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var code0 = gen.Generate(linkId, userId, attempt: 0);
        var code1 = gen.Generate(linkId, userId, attempt: 1);
        var code2 = gen.Generate(linkId, userId, attempt: 2);

        Assert.NotEqual(code0, code1);
        Assert.NotEqual(code1, code2);
    }

    [Fact]
    public void Generate_NullUserId_DoesNotThrow()
    {
        var gen = Make(8);
        var code = gen.Generate(Guid.CreateVersion7(), discriminator: null, attempt: 0);
        Assert.Equal(8, code.Length);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    public void Generate_HonorsLengthOption(int length)
    {
        var code = Make(length).Generate(Guid.CreateVersion7(), Guid.CreateVersion7());
        Assert.Equal(length, code.Length);
    }
}
