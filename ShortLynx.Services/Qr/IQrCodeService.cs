namespace ShortLynx.Services.Qr;

/// <summary>
/// Renders QR codes for short links. Both formats are produced with no native dependencies, so the
/// slim Linux runtime image needs nothing extra: PNG for embedding/printing, SVG for scalable use.
/// </summary>
public interface IQrCodeService
{
    /// <summary>PNG bytes encoding <paramref name="content"/>. <paramref name="pixelsPerModule"/> scales the image.</summary>
    byte[] GeneratePng(string content, int pixelsPerModule = 10);

    /// <summary>An SVG document (as a string) encoding <paramref name="content"/>.</summary>
    string GenerateSvg(string content, int pixelsPerModule = 10);
}
