using ShortLynx.Services.Qr;

namespace ShortLynx.Tests.Services.Qr;

public class QrCodeServiceTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly QrCodeService _svc = new();

    [Fact]
    public void GeneratePng_ReturnsPngBytes()
    {
        var bytes = _svc.GeneratePng("https://shrtlynx.com/abc123");

        Assert.NotEmpty(bytes);
        Assert.True(bytes.Length > PngSignature.Length);
        Assert.Equal(PngSignature, bytes[..PngSignature.Length]); // PNG magic header
    }

    [Fact]
    public void GenerateSvg_ReturnsSvgMarkup()
    {
        var svg = _svc.GenerateSvg("https://shrtlynx.com/abc123");

        Assert.Contains("<svg", svg);
        Assert.Contains("</svg>", svg);
    }

    [Fact]
    public void Generate_IsDeterministic_ForSameContent()
    {
        const string url = "https://shrtlynx.com/abc123";
        Assert.Equal(_svc.GeneratePng(url), _svc.GeneratePng(url));
        Assert.Equal(_svc.GenerateSvg(url), _svc.GenerateSvg(url));
    }

    [Fact]
    public void GeneratePng_DifferentContent_ProducesDifferentBytes()
    {
        var a = _svc.GeneratePng("https://shrtlynx.com/aaa");
        var b = _svc.GeneratePng("https://shrtlynx.com/bbb");
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void GeneratePng_LargerModuleSize_ProducesLargerImage()
    {
        var small = _svc.GeneratePng("https://shrtlynx.com/abc123", pixelsPerModule: 4);
        var large = _svc.GeneratePng("https://shrtlynx.com/abc123", pixelsPerModule: 20);
        Assert.True(large.Length > small.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Generate_EmptyContent_Throws(string content)
    {
        Assert.Throws<ArgumentException>(() => _svc.GeneratePng(content));
        Assert.Throws<ArgumentException>(() => _svc.GenerateSvg(content));
    }
}
