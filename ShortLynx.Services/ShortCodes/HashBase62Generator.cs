using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ShortLynx.Services.ShortCodes;

// Deterministic code derived from (linkId, discriminator, attempt). Same inputs always produce the same
// candidate code, supporting idempotent generation. Callers increment `attempt` on collision until a
// unique code lands — note that a null discriminator only has `attempt` to vary on, so a link's shared
// code has a small space; distinct per-recipient/per-post codes come from the discriminator, not attempt.
public sealed class HashBase62Generator(IOptions<ShortCodeOptions> options) : IShortCodeGenerator
{
    public string Generate(Guid linkId, Guid? discriminator = null, int attempt = 0)
    {
        var input = $"{linkId}|{discriminator}|{attempt}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Base62.Encode(hash, options.Value.Length);
    }
}
