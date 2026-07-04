using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ShortLynx.Services.Social;

/// <summary>The payload Meta signs into a deauthorize/data-deletion callback's <c>signed_request</c>.</summary>
public sealed record MetaSignedRequestPayload(
    [property: JsonPropertyName("user_id")] string UserId,
    [property: JsonPropertyName("algorithm")] string Algorithm,
    [property: JsonPropertyName("issued_at")] long IssuedAt);

/// <summary>
/// Verifies and parses the <c>signed_request</c> form field Meta POSTs to the uninstall (deauthorize)
/// and data-deletion webhook callbacks. Format: <c>{base64url(hmac-sig)}.{base64url(json-payload)}</c>.
/// The signature is HMAC-SHA256 over the still-encoded payload segment, keyed with the app secret — this
/// is the only thing standing between "a real Meta callback" and "anyone can POST a fake user_id and
/// make us delete someone else's connection," so it is verified, not just decoded.
/// </summary>
public static class MetaSignedRequestParser
{
    public static bool TryParse(string? signedRequest, string appSecret, out MetaSignedRequestPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(signedRequest) || string.IsNullOrEmpty(appSecret))
            return false;

        var parts = signedRequest.Split('.');
        if (parts.Length != 2) return false;

        byte[] signature, payloadBytes;
        try
        {
            signature = Base64UrlDecode(parts[0]);
            payloadBytes = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        // HMAC is computed over the encoded (not decoded) payload segment, per Meta's spec.
        var expectedSignature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), Encoding.UTF8.GetBytes(parts[1]));
        if (!CryptographicOperations.FixedTimeEquals(signature, expectedSignature))
            return false;

        MetaSignedRequestPayload? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<MetaSignedRequestPayload>(payloadBytes);
        }
        catch (JsonException)
        {
            return false;
        }

        if (parsed is null || !string.Equals(parsed.Algorithm, "HMAC-SHA256", StringComparison.OrdinalIgnoreCase))
            return false;

        payload = parsed;
        return true;
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var s = value.Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');
        return Convert.FromBase64String(s);
    }
}
