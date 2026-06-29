using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace ShortLynx.Services.ShortCodes;

// Mode 1 — generates a cryptographically random Base62 code. linkId, userId,
// and attempt are intentionally ignored; each call produces an independent code.
public sealed class RandomBase62Generator(IOptions<ShortCodeOptions> options) : IShortCodeGenerator
{
    public string Generate(Guid linkId, Guid? userId = null, int attempt = 0)
    {
        var length = options.Value.Length;
        var bytes = RandomNumberGenerator.GetBytes(length);
        return Base62.Encode(bytes, length);
    }
}
