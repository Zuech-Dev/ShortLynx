namespace ShortLynx.Services.Social;

/// <summary>
/// Encrypts/decrypts sensitive strings (OAuth access/refresh tokens) for storage at rest. Implemented in
/// the composition root over ASP.NET DataProtection; the interface keeps the services + storage layers
/// free of a framework dependency and lets an external KMS be swapped in later.
/// </summary>
public interface ITokenProtector
{
    /// <summary>Encrypts plaintext to opaque, storable ciphertext.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts ciphertext produced by <see cref="Protect"/>. Throws if it can't be decrypted.</summary>
    string Unprotect(string protectedText);
}
