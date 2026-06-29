using QRCoder;

namespace ShortLynx.Services.Qr;

public sealed class QrCodeService : IQrCodeService
{
    // Pixels-per-module bounds keep the output a reasonable size (a tiny value is unscannable, a huge
    // one wastes bandwidth). Callers pass a requested size; we clamp rather than reject.
    private const int MinModule = 2;
    private const int MaxModule = 40;

    public byte[] GeneratePng(string content, int pixelsPerModule = 10)
    {
        using var data = Create(content);
        return new PngByteQRCode(data).GetGraphic(Clamp(pixelsPerModule));
    }

    public string GenerateSvg(string content, int pixelsPerModule = 10)
    {
        using var data = Create(content);
        return new SvgQRCode(data).GetGraphic(Clamp(pixelsPerModule));
    }

    private static QRCodeData Create(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("QR content must not be empty.", nameof(content));

        using var generator = new QRCodeGenerator();
        // ECC level M (~15% recovery) balances density and scan robustness for short URLs.
        return generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
    }

    private static int Clamp(int pixelsPerModule) => Math.Clamp(pixelsPerModule, MinModule, MaxModule);
}
