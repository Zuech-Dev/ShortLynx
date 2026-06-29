using ShortLynx.Services.UrlValidation;

namespace ShortLynx.Tests.Infrastructure;

internal sealed class StubUrlValidationService(bool isValid = true, string? reason = null) : IUrlValidationService
{
    public Task<ValidationResult> ValidateAsync(string url)
        => Task.FromResult(isValid ? ValidationResult.Ok() : ValidationResult.Fail(reason ?? "blocked"));
}
