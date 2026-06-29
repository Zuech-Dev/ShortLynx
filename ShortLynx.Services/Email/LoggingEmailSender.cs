using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ShortLynx.Services.Email;

/// <summary>
/// Writes emails to the log instead of sending them — so magic links to addresses no real provider can
/// deliver to (e.g. invited test users) are still retrievable from the console during development.
/// </summary>
public sealed partial class LoggingEmailSender(ILogger<LoggingEmailSender> logger) : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
    {
        // Surface any link in the body prominently (the magic-link URL), plus the full body as a fallback.
        var link = LinkPattern().Match(htmlBody) is { Success: true } m ? m.Groups[1].Value : "(no link found)";
        logger.LogWarning(
            "[DEV EMAIL] To: {To} | Subject: {Subject}\n  Link: {Link}\n  Body: {Body}",
            to, subject, link, htmlBody);
        return Task.CompletedTask;
    }

    [GeneratedRegex("href=\"([^\"]+)\"", RegexOptions.IgnoreCase)]
    private static partial Regex LinkPattern();
}
