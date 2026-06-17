namespace ShortLynx.Services.Email;

/// <summary>Bound from the "Resend" config section. ApiKey belongs in user-secrets / env, not appsettings.</summary>
public sealed class ResendOptions
{
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Sender address. Must be on a Resend-verified domain in production. For local testing,
    /// "onboarding@resend.dev" works but only delivers to your own Resend account email.
    /// </summary>
    public string FromAddress { get; set; } = "onboarding@resend.dev";

    public string FromName { get; set; } = "ShortLynx";
}
