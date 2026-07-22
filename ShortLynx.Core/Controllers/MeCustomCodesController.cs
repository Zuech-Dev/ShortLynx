using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using ShortLynx.Core.RateLimit;
using ShortLynx.Services.Entitlements;
using ShortLynx.Services.ShortCodes;

namespace ShortLynx.Core.Controllers;

/// <summary>Custom (vanity) short-code availability for the current account's dashboard/frontend.</summary>
[Route("me/custom-code")]
public class MeCustomCodesController(ICustomCodeService customCodes, IEntitlements entitlements)
    : SessionControllerBase
{
    // GET /me/custom-code/check?code=my-code
    // Debounced by the caller. Entitlement-gated (402 if the account can't mint custom codes at all —
    // e.g. Free tier, or a hard cap with no overage) and rate-limited (enumeration guard).
    [HttpGet("check")]
    [EnableRateLimiting(RateLimitPolicies.CustomCodeCheck)]
    public async Task<IActionResult> Check([FromQuery] string? code, CancellationToken ct)
    {
        if (!await entitlements.CanCreateCustomCodeAsync(AccountId, ct))
            return StatusCode(StatusCodes.Status402PaymentRequired,
                new { error = "Custom codes aren't available on your current plan." });

        var result = await customCodes.CheckAsync(code, ct);
        return Ok(new
        {
            available = result.IsAvailable,
            status = result.Status.ToString(),
            reason = result.Reason,
        });
    }
}
