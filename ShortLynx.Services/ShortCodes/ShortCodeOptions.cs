namespace ShortLynx.Services.ShortCodes;

public class ShortCodeOptions
{
    /// <summary>Length of a generated (random) code.</summary>
    public int Length { get; set; } = 8;

    // ── Custom (vanity) codes ───────────────────────────────────────────────────
    // The minimum length is a fixed 8 (see CustomCodeValidator.MinLength); only the maximum is
    // configurable. Codes are lowercase a–z0–9 with internal single hyphens.

    /// <summary>Maximum length of a custom code (min is a fixed 8). Env: <c>ShortCode__CustomCodeMaxLength</c>.</summary>
    public int CustomCodeMaxLength { get; set; } = 12;

    /// <summary>
    /// URL segment custom codes live under (default <c>c</c> → <c>/c/&lt;code&gt;</c>), isolating them
    /// from the root <c>/{code}</c> namespace. Env: <c>ShortCode__CustomRoutePrefix</c>. Slashes are
    /// trimmed at use.
    /// </summary>
    public string CustomRoutePrefix { get; set; } = "c";

    /// <summary>
    /// Terms a custom code may not equal (impersonation / brand protection). Extends the always-reserved
    /// system routes. Overridable via <c>ShortCode__ImpersonationTerms__0</c>, etc.
    /// </summary>
    public string[] ImpersonationTerms { get; set; } =
    [
        "admin", "administrator", "login", "logout", "signin", "signup", "auth", "oauth",
        "api", "support", "help", "billing", "account", "accounts", "settings", "dashboard",
        "shrtlynx", "shortlynx",
    ];

    /// <summary>
    /// Optional path to a newline-delimited profanity wordlist (substring match). When unset, a small
    /// bundled default list is used. Env: <c>ShortCode__ProfanityListPath</c>.
    /// </summary>
    public string? ProfanityListPath { get; set; }
}
