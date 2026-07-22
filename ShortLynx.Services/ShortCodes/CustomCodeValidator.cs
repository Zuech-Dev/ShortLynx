using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace ShortLynx.Services.ShortCodes;

/// <summary>Outcome of validating a proposed custom code. <see cref="Reason"/> is null when valid.</summary>
public sealed record CustomCodeValidation(bool IsValid, string? Reason)
{
    public static readonly CustomCodeValidation Valid = new(true, null);
    public static CustomCodeValidation Invalid(string reason) => new(false, reason);
}

/// <summary>
/// Validates operator-chosen (vanity) codes and produces their canonical (normalized) form. Pure and
/// deterministic — shared by the availability check and the create path so the rules never diverge.
/// Rules: lowercase <c>a–z0–9</c> with single internal hyphens; length 8..<see cref="ShortCodeOptions.CustomCodeMaxLength"/>;
/// not a reserved system route, impersonation term, or profanity.
/// </summary>
public sealed partial class CustomCodeValidator
{
    /// <summary>Minimum length is a fixed 8 (only the maximum is configurable).</summary>
    public const int MinLength = 8;

    // Lowercase alphanumerics with single hyphens *between* groups. Rejects leading/trailing/consecutive
    // hyphens by construction. Assumes the input is already normalized (trimmed + lowercased).
    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex CodePattern();

    // Always reserved, independent of config — these are real routes the redirect site serves, plus the
    // custom prefix segment itself.
    private static readonly HashSet<string> SystemRoutes =
        new(StringComparer.Ordinal) { "health", "privacy", "error", "disclosure", "not-found" };

    private readonly ShortCodeOptions _opts;
    private readonly HashSet<string> _impersonation;
    private readonly IReadOnlyList<string> _profanity;

    public CustomCodeValidator(IOptions<ShortCodeOptions> options)
    {
        _opts = options.Value;
        _impersonation = new HashSet<string>(
            _opts.ImpersonationTerms.Select(t => t.Trim().ToLowerInvariant()).Where(t => t.Length > 0),
            StringComparer.Ordinal);
        _profanity = LoadProfanity(_opts.ProfanityListPath);
    }

    /// <summary>Trim + lowercase — the canonical stored/compared form.</summary>
    public static string Normalize(string code) => code.Trim().ToLowerInvariant();

    /// <summary>Validates <paramref name="rawCode"/>. On success the caller should store <see cref="Normalize"/>(rawCode).</summary>
    public CustomCodeValidation Validate(string? rawCode)
    {
        if (string.IsNullOrWhiteSpace(rawCode))
            return CustomCodeValidation.Invalid("A custom code is required.");

        var code = Normalize(rawCode);
        var max = Math.Max(MinLength, _opts.CustomCodeMaxLength);

        if (code.Length < MinLength)
            return CustomCodeValidation.Invalid($"Custom code must be at least {MinLength} characters.");
        if (code.Length > max)
            return CustomCodeValidation.Invalid($"Custom code must be at most {max} characters.");
        if (!CodePattern().IsMatch(code))
            return CustomCodeValidation.Invalid("Use lowercase letters, numbers, and single hyphens between them.");

        // Reserved / brand checks compare against the whole code and its hyphen-separated segments, so
        // "my-admin-link" is caught, not just "admin".
        if (SystemRoutes.Contains(code) || code == _opts.CustomRoutePrefix.Trim('/').ToLowerInvariant())
            return CustomCodeValidation.Invalid("That code is reserved.");

        var segments = code.Split('-');
        if (segments.Any(_impersonation.Contains))
            return CustomCodeValidation.Invalid("That code is reserved.");

        if (_profanity.Any(bad => code.Contains(bad, StringComparison.Ordinal)))
            return CustomCodeValidation.Invalid("That code isn't allowed.");

        return CustomCodeValidation.Valid;
    }

    private static IReadOnlyList<string> LoadProfanity(string? path)
    {
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            return File.ReadAllLines(path)
                .Select(l => l.Trim().ToLowerInvariant())
                .Where(l => l.Length > 0 && !l.StartsWith('#'))
                .ToArray();
        }
        // Small bundled default — deliberately minimal; operators override via ProfanityListPath.
        return DefaultProfanity;
    }

    private static readonly string[] DefaultProfanity =
        ["fuck", "shit", "cunt", "nigger", "faggot", "bitch", "asshole", "bastard", "dick", "porn"];
}
