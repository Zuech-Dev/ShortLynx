namespace ShortLynx.Services.ShortCodes;

internal static class Base62
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";

    // Maps the first `length` bytes of the source span to Base62 characters.
    // Each byte is reduced mod 62. The resulting distribution has a slight bias
    // (values 0-7 are marginally more likely) that is acceptable for short codes.
    internal static string Encode(ReadOnlySpan<byte> bytes, int length)
    {
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[bytes[i] % 62];
        return new string(chars);
    }

    internal static bool IsValidCode(string code)
        => code.All(c => Alphabet.Contains(c));
}
