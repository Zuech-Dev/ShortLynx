using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;

namespace ShortLynx.Web.Pages;

public class DataDeletionModel(IConfiguration configuration) : PageModel
{
    public DateOnly LastUpdated { get; } = new(2026, 7, 3);

    public string ContactEmail { get; private set; } = "privacy@shrtlynx.com";

    public void OnGet()
        => ContactEmail = configuration["Legal:PrivacyContactEmail"] ?? ContactEmail;
}
