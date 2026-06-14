namespace ShortLynx.Services.MagicLinks;

public class MagicLinkOptions
{
    public int TokenExpiryMinutes { get; set; } = 15;

    /// <summary>Base URL for the confirm endpoint, e.g. "https://example.com/auth/confirm". Token is appended as ?token=...</summary>
    public string ConfirmationUrlBase { get; set; } = string.Empty;

    /// <summary>
    /// Max concurrently-valid (unused, unexpired) tokens per email. Once reached, further requests are
    /// silently dropped (no token created, no email sent) to prevent email bombing a single address.
    /// </summary>
    public int MaxActiveTokensPerUser { get; set; } = 3;
}
