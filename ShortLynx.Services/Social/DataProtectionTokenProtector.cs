using Microsoft.AspNetCore.DataProtection;

namespace ShortLynx.Services.Social;

/// <summary>
/// <see cref="ITokenProtector"/> over ASP.NET DataProtection. The protector purpose namespaces the keys
/// so social tokens can't be cross-decrypted with any other protected payload.
///
/// Every app that protects or unprotects tokens must share ONE key ring: the composition roots call
/// <c>AddDataProtection().SetApplicationName("ShortLynx").PersistKeysToDbContext&lt;ShortLynxDbContext&gt;()</c>,
/// which persists keys in the database — shared across Core/Admin and stable across redeploys.
/// </summary>
public sealed class DataProtectionTokenProtector : ITokenProtector
{
    private readonly IDataProtector _protector;

    public DataProtectionTokenProtector(IDataProtectionProvider provider)
        => _protector = provider.CreateProtector("ShortLynx.SocialTokens.v1");

    public string Protect(string plaintext) => _protector.Protect(plaintext);

    public string Unprotect(string protectedText) => _protector.Unprotect(protectedText);
}
