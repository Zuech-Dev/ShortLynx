namespace ShortLynx.Services.UrlValidation;

public sealed record ValidationResult(bool IsValid, string? Reason = null)
{
    public static ValidationResult Ok() => new(true);
    public static ValidationResult Fail(string reason) => new(false, reason);
}
