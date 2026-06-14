using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.RateLimit;
using ShortLynx.Services.MagicLinks;

namespace ShortLynx.Core.Controllers;

[ApiController]
[Route("magic-links")]
[AllowAnonymous]
[EnableRateLimiting(RateLimitPolicies.MagicLinks)]
public class MagicLinksController(IMagicLinkService magicLinkService) : ControllerBase
{
    /// <summary>
    /// Triggers a magic-link email for the given address. Always returns 204 to prevent
    /// email-existence enumeration — callers should not assume success means the email exists.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Request(
        [FromBody] RequestMagicLinkRequest request,
        CancellationToken ct)
    {
        await magicLinkService.CreateTokenAsync(request.Email, ct);
        return NoContent();
    }
}
