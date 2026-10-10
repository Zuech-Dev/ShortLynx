using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortLynx.Core.Auth;
using ShortLynx.Data.Context;
using ShortLynx.Services.Visits;

namespace ShortLynx.Core.Controllers;

/// <summary>
/// One-off data maintenance for super-admins: operations that depend on facts about the deployment only
/// its operator knows, so they never run on their own.
/// </summary>
[ApiController]
[Route("admin/maintenance")]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Policy = AuthorizationPolicies.SuperAdmin)]
public class AdminMaintenanceController(ShortLynxDbContext db) : ControllerBase
{
    public sealed record ReclassifyLinkScannersRequest(DateTimeOffset? Before, bool DryRun = true);

    // POST /admin/maintenance/reclassify-link-scanners — marks clicks before `before` that a link scanner
    // would have produced (browser UA, no Sec-Fetch-Site) as SuspectedAutomated, as the visit writer has
    // done for new clicks since pkg-v0.9.11. Only valid for a period when redirects were served over
    // HTTPS: over plain HTTP real browsers omit Sec-Fetch-Site too. Dry run by default; idempotent.
    [HttpPost("reclassify-link-scanners")]
    public async Task<IActionResult> ReclassifyLinkScanners(ReclassifyLinkScannersRequest req, CancellationToken ct)
    {
        if (req.Before is not { } before)
            return BadRequest(new { error = "`before` is required: the end of the period your redirects were served over HTTPS." });
        if (before > DateTimeOffset.UtcNow)
            return BadRequest(new { error = "`before` can't be in the future." });

        return Ok(await LinkScannerReclassifier.RunAsync(db, before, req.DryRun, ct));
    }
}
