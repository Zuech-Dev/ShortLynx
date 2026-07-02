using Microsoft.AspNetCore.DataProtection;
using ShortLynx.Services.Social;

namespace ShortLynx.Core.Social;

/// <summary>
/// <see cref="ITokenProtector"/> over ASP.NET DataProtection. The protector purpose namespaces the keys so
/// social tokens can't be cross-decrypted with any other protected payload.
///
/// ⚠️ Operational note: DataProtection keys must be **persisted** in production (e.g. a mounted volume via
/// DataProtection:KeyPath). With ephemeral keys, every restart/redeploy rotates the key ring and stored
/// tokens become undecryptable — connections would need re-authing. See DEPLOY notes.
/// </summary>
public sealed class DataProtectionTokenProtector : ITokenProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionTokenProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("ShortLynx.SocialTokens.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
