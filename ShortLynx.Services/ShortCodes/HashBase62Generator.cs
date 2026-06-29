using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ShortLynx.Services.ShortCodes;

// Mode 2 — deterministic code derived from (linkId, userId, attempt).
// Same inputs always produce the same candidate code, supporting idempotent
// generation. Callers increment `attempt` on collision until a unique code lands.
public sealed class HashBase62Generator(IOptions<ShortCodeOptions> options) : IShortCodeGenerator
{
    public string Generate(Guid linkId, Guid? userId = null, int attempt = 0)
    {
        var input = $"{linkId}|{userId}|{attempt}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Base62.Encode(hash, options.Value.Length);
    }
}
