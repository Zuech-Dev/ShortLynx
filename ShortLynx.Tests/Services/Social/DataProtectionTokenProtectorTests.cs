using Microsoft.AspNetCore.DataProtection;
using ShortLynx.Core.Social;

namespace ShortLynx.Tests.Services.Social;

public class DataProtectionTokenProtectorTests
{
    private static DataProtectionTokenProtector Make()
        => new(DataProtectionProvider.Create("ShortLynx.Tests"));

    [Fact]
    public void Protect_ThenUnprotect_RoundTrips()
    {
        var sut = Make();
        const string token = "did:plc:secret-access-token-value";

        var protectedText = sut.Protect(token);

        Assert.NotEqual(token, protectedText);           // stored form is ciphertext, not plaintext
        Assert.Equal(token, sut.Unprotect(protectedText));
    }

    [Fact]
    public void Unprotect_TamperedCiphertext_Throws()
    {
        var sut = Make();
        Assert.ThrowsAny<Exception>(() => sut.Unprotect("not-a-valid-protected-payload"));
    }
}
