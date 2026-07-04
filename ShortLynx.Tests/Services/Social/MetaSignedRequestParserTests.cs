using System.Security.Cryptography;
using System.Text;
using ShortLynx.Services.Social;

namespace ShortLynx.Tests.Services.Social;

public class MetaSignedRequestParserTests
{
    private const string AppSecret = "test-app-secret";

    // Mirrors Meta's own construction so tests exercise the real wire format, not a simplified stand-in.
    private static string Build(string json, string secret = AppSecret)
    {
        var payload = Base64UrlEncode(Encoding.UTF8.GetBytes(json));
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(payload));
        return $"{Base64UrlEncode(signature)}.{payload}";
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Fact]
    public void TryParse_ValidSignedRequest_ReturnsPayload()
    {
        var sr = Build("""{"user_id":"17800000000000000","algorithm":"HMAC-SHA256","issued_at":1735689600}""");

        var ok = MetaSignedRequestParser.TryParse(sr, AppSecret, out var payload);

        Assert.True(ok);
        Assert.Equal("17800000000000000", payload!.UserId);
        Assert.Equal("HMAC-SHA256", payload.Algorithm);
    }

    [Fact]
    public void TryParse_LowercaseAlgorithm_StillValid()
    {
        // Meta's own reference implementation compares algorithm case-insensitively.
        var sr = Build("""{"user_id":"1","algorithm":"hmac-sha256","issued_at":1}""");

        Assert.True(MetaSignedRequestParser.TryParse(sr, AppSecret, out _));
    }

    [Fact]
    public void TryParse_TamperedPayload_Rejected()
    {
        var sr = Build("""{"user_id":"17800000000000000","algorithm":"HMAC-SHA256","issued_at":1735689600}""");
        var parts = sr.Split('.');
        // Swap in a different (still validly-encoded) payload without re-signing — the classic forgery attempt.
        var forgedPayload = Base64UrlEncode(Encoding.UTF8.GetBytes(
            """{"user_id":"99999999999999999","algorithm":"HMAC-SHA256","issued_at":1735689600}"""));
        var forged = $"{parts[0]}.{forgedPayload}";

        Assert.False(MetaSignedRequestParser.TryParse(forged, AppSecret, out var payload));
        Assert.Null(payload);
    }

    [Fact]
    public void TryParse_WrongAppSecret_Rejected()
    {
        var sr = Build("""{"user_id":"1","algorithm":"HMAC-SHA256","issued_at":1}""");

        Assert.False(MetaSignedRequestParser.TryParse(sr, "a-different-secret", out _));
    }

    [Fact]
    public void TryParse_UnsupportedAlgorithm_Rejected()
    {
        var sr = Build("""{"user_id":"1","algorithm":"MD5","issued_at":1}""");

        Assert.False(MetaSignedRequestParser.TryParse(sr, AppSecret, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-a-signed-request")]
    [InlineData("only.one.dot.too.many")]
    public void TryParse_MalformedInput_Rejected(string? input)
    {
        Assert.False(MetaSignedRequestParser.TryParse(input, AppSecret, out var payload));
        Assert.Null(payload);
    }

    [Fact]
    public void TryParse_EmptyAppSecret_Rejected()
    {
        var sr = Build("""{"user_id":"1","algorithm":"HMAC-SHA256","issued_at":1}""");

        Assert.False(MetaSignedRequestParser.TryParse(sr, "", out _));
    }
}
