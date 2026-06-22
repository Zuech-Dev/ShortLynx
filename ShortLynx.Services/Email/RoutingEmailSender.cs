using Microsoft.Extensions.Options;

namespace ShortLynx.Services.Email;

/// <summary>
/// Routes each email to <paramref name="primary"/> when the recipient's domain is deliverable, otherwise
/// to <paramref name="fallback"/>. Used for Hybrid delivery (real sends to your verified domain, logged
/// links for everything else).
/// </summary>
public sealed class RoutingEmailSender(
    IEmailSender primary,
    IEmailSender fallback,
    IOptions<EmailDeliveryOptions> options) : IEmailSender
{
    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken ct = default)
        => (options.Value.IsDeliverable(to) ? primary : fallback).SendAsync(to, subject, htmlBody, ct);
}
