namespace ShortLynx.Services.Email;

/// <summary>
/// Selects how outgoing email is delivered. Bound from the "Email" configuration section.
/// </summary>
public sealed class EmailDeliveryOptions
{
    /// <summary>
    /// <c>Resend</c> (default) sends everything via Resend; <c>Log</c> writes every message to the log
    /// (no real send — useful for local dev); <c>Hybrid</c> sends to <see cref="DeliverableDomains"/> via
    /// Resend and logs the rest (so magic links to addresses Resend can't deliver still appear in the log).
    /// </summary>
    public string Mode { get; set; } = "Resend";

    /// <summary>Recipient domains Resend can deliver to (Hybrid mode). Everything else is logged.</summary>
    public string[] DeliverableDomains { get; set; } = [];

    public bool IsDeliverable(string toEmail)
    {
        var at = toEmail.LastIndexOf('@');
        if (at < 0) return false;
        var domain = toEmail[(at + 1)..].Trim();
        return DeliverableDomains.Contains(domain, StringComparer.OrdinalIgnoreCase);
    }
}
