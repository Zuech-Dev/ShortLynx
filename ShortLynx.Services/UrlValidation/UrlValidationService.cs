using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace ShortLynx.Services.UrlValidation;

public sealed class UrlValidationService : IUrlValidationService
{
    private static readonly HashSet<string> AllowedSchemes = ["http", "https"];
    private readonly HashSet<string> _blocklist;

    public UrlValidationService(IOptions<UrlValidationOptions> options)
        => _blocklist = LoadBlocklist(options.Value.BlocklistPath);

    public async Task<ValidationResult> ValidateAsync(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return ValidationResult.Fail("Invalid URL format.");

        if (!AllowedSchemes.Contains(uri.Scheme))
            return ValidationResult.Fail($"Scheme '{uri.Scheme}' is not allowed; use http or https.");

        var host = uri.Host;

        if (_blocklist.Contains(host.ToLowerInvariant()))
            return ValidationResult.Fail($"Domain '{host}' is on the blocklist.");

        // Resolve to IP(s) — handles both IP literals and hostnames.
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host);
        }
        catch (Exception ex)
        {
            return ValidationResult.Fail($"Cannot resolve host '{host}': {ex.Message}");
        }

        foreach (var ip in addresses)
        {
            if (IsPrivate(ip))
                return ValidationResult.Fail($"SSRF: '{host}' resolves to a private or loopback address.");
        }

        return ValidationResult.Ok();
    }

    internal static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return b[0] == 127                                          // 127.0.0.0/8  loopback
                || b[0] == 10                                           // 10.0.0.0/8
                || (b[0] == 172 && b[1] is >= 16 and <= 31)            // 172.16.0.0/12
                || (b[0] == 192 && b[1] == 168)                        // 192.168.0.0/16
                || (b[0] == 169 && b[1] == 254);                       // 169.254.0.0/16 link-local
        }

        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.Equals(IPAddress.IPv6Loopback) || ip.IsIPv6LinkLocal;

        return false;
    }

    private static HashSet<string> LoadBlocklist(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return [];

        return [..File.ReadAllLines(path)
            .Select(l => l.Trim().ToLowerInvariant())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))];
    }
}
