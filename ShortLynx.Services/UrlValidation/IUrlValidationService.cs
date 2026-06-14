namespace ShortLynx.Services.UrlValidation;

public interface IUrlValidationService
{
    Task<ValidationResult> ValidateAsync(string url);
}
