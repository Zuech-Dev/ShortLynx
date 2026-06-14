using System.Net;
using Microsoft.Extensions.Options;
using ShortLynx.Services.UrlValidation;

namespace ShortLynx.Tests.Services.UrlValidation;

public class UrlValidationServiceTests
{
    private static UrlValidationService Make(string? blocklistPath = null)
        => new(Options.Create(new UrlValidationOptions { BlocklistPath = blocklistPath }));

    // ── Format / scheme ───────────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_MalformedUrl()
    {
        var result = await Make().ValidateAsync("not a url");
        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task Rejects_EmptyString()
    {
        var result = await Make().ValidateAsync("");
        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("ftp://example.com/file")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,hello")]
    public async Task Rejects_DisallowedScheme(string url)
    {
        var result = await Make().ValidateAsync(url);
        Assert.False(result.IsValid);
        Assert.Contains("Scheme", result.Reason);
    }

    [Theory]
    [InlineData("http://1.2.3.4/path")]
    [InlineData("https://1.2.3.4/path")]
    public async Task Accepts_HttpAndHttps(string url)
    {
        var result = await Make().ValidateAsync(url);
        Assert.True(result.IsValid, result.Reason);
    }

    // ── SSRF — IPv4 private ranges (IP literals, no DNS needed) ──────────────

    [Theory]
    [InlineData("http://127.0.0.1/")]          // loopback
    [InlineData("http://127.1.2.3/")]          // loopback range
    [InlineData("http://10.0.0.1/")]           // 10/8
    [InlineData("http://10.255.255.255/")]     // 10/8 upper bound
    [InlineData("http://172.16.0.1/")]         // 172.16/12
    [InlineData("http://172.31.255.255/")]     // 172.16/12 upper bound
    [InlineData("http://192.168.0.1/")]        // 192.168/16
    [InlineData("http://192.168.255.255/")]    // 192.168/16 upper bound
    [InlineData("http://169.254.0.1/")]        // link-local
    [InlineData("http://169.254.169.254/")]    // AWS metadata service
    public async Task Rejects_PrivateIpv4Literal(string url)
    {
        var result = await Make().ValidateAsync(url);
        Assert.False(result.IsValid);
        Assert.Contains("SSRF", result.Reason);
    }

    // ── SSRF — IPv6 ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("http://[::1]/")]          // IPv6 loopback
    [InlineData("http://[fe80::1]/")]      // IPv6 link-local
    public async Task Rejects_PrivateIpv6Literal(string url)
    {
        var result = await Make().ValidateAsync(url);
        Assert.False(result.IsValid);
        Assert.Contains("SSRF", result.Reason);
    }

    // ── SSRF — hostname that resolves to loopback ─────────────────────────────

    [Fact]
    public async Task Rejects_LocalhostHostname()
    {
        // 'localhost' is defined in /etc/hosts as 127.0.0.1 on every OS.
        var result = await Make().ValidateAsync("http://localhost/");
        Assert.False(result.IsValid);
    }

    // ── Public IP accepted ────────────────────────────────────────────────────

    [Theory]
    [InlineData("https://1.1.1.1/")]    // Cloudflare public DNS
    [InlineData("https://8.8.8.8/")]    // Google public DNS
    public async Task Accepts_PublicIpLiteral(string url)
    {
        var result = await Make().ValidateAsync(url);
        Assert.True(result.IsValid, result.Reason);
    }

    // ── Blocklist ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rejects_BlocklistedDomain()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "bad.example.com\nmalware.test\n");
            var result = await Make(path).ValidateAsync("https://bad.example.com/path");
            Assert.False(result.IsValid);
            Assert.Contains("blocklist", result.Reason, StringComparison.OrdinalIgnoreCase);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Blocklist_MatchingIsCaseInsensitive()
    {
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(path, "Bad.Example.Com\n");
            var result = await Make(path).ValidateAsync("https://BAD.EXAMPLE.COM/");
            Assert.False(result.IsValid);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Blocklist_CommentLines_AreIgnored()
    {
        var path = Path.GetTempFileName();
        try
        {
            // Only the comment line; domain is NOT blocked.
            await File.WriteAllTextAsync(path, "# this is a comment\n");
            var result = await Make(path).ValidateAsync("https://1.2.3.4/");
            Assert.True(result.IsValid, result.Reason);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Blocklist_MissingFile_IsIgnored()
    {
        // Non-existent path should not throw; validation proceeds normally.
        var result = await Make("/tmp/does-not-exist-shortlynx.txt").ValidateAsync("https://1.2.3.4/");
        Assert.True(result.IsValid, result.Reason);
    }

    // ── IsPrivate helper (unit) ───────────────────────────────────────────────

    [Theory]
    [InlineData("127.0.0.1", true)]
    [InlineData("10.0.0.1", true)]
    [InlineData("172.16.0.1", true)]
    [InlineData("172.31.255.255", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("169.254.1.1", true)]
    [InlineData("1.1.1.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("203.0.113.1", false)]
    public void IsPrivate_IPv4(string ip, bool expected)
    {
        Assert.Equal(expected, UrlValidationService.IsPrivate(IPAddress.Parse(ip)));
    }

    [Theory]
    [InlineData("::1", true)]
    [InlineData("fe80::1", true)]
    [InlineData("2001:db8::1", false)]
    public void IsPrivate_IPv6(string ip, bool expected)
    {
        Assert.Equal(expected, UrlValidationService.IsPrivate(IPAddress.Parse(ip)));
    }
}
