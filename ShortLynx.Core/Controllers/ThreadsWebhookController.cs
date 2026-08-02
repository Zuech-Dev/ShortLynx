using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ShortLynx.Data.Context;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Social;

namespace ShortLynx.Core.Controllers;

/// <summary>
/// Meta's server-to-server callbacks for the Threads integration — deliberately a plain
/// <see cref="ControllerBase"/>, not <see cref="SessionControllerBase"/>: these calls carry no user
/// session at all, and <c>SessionControllerBase</c>'s class-level <c>ValidateSessionClaimsAttribute</c>
/// is a plain action filter that runs regardless of <see cref="AllowAnonymousAttribute"/> (that
/// attribute only short-circuits the *authorization* stage), so it would reject every call anyway if
/// this inherited from it. Unauthenticated by design — the HMAC verification in
/// <see cref="MetaSignedRequestParser"/> is the only thing standing between this and anyone deleting
/// anyone's connection.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class ThreadsWebhookController(
    ShortLynxDbContext db,
    IOptions<ThreadsOptions> threadsOptions) : ControllerBase
{
    // Meta POSTs a signed_request here when a user removes ShortLynx from their Threads app settings.
    [HttpPost("webhooks/threads/deauthorize")]
    public async Task<IActionResult> Deauthorize(CancellationToken ct)
    {
        var form = await Request.ReadFormAsync(ct);
        if (!MetaSignedRequestParser.TryParse(form["signed_request"], threadsOptions.Value.AppSecret, out var payload))
            return BadRequest();

        await db.SocialConnectionEntities
            .Where(c => c.Platform == SocialPlatform.Threads && c.ExternalAccountId == payload!.UserId)
            .ExecuteDeleteAsync(ct);

        return Ok();
    }

    // Meta POSTs a signed_request here when a user requests deletion of their data via Meta's own UI
    // (Settings → Apps and Websites). Must respond with the exact { url, confirmation_code } shape
    // Meta's Data Deletion Callback spec requires.
    [HttpPost("webhooks/threads/delete")]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        var form = await Request.ReadFormAsync(ct);
        if (!MetaSignedRequestParser.TryParse(form["signed_request"], threadsOptions.Value.AppSecret, out var payload))
            return BadRequest();

        await db.SocialConnectionEntities
            .Where(c => c.Platform == SocialPlatform.Threads && c.ExternalAccountId == payload!.UserId)
            .ExecuteDeleteAsync(ct);

        var confirmationCode = Guid.NewGuid().ToString("N")[..16];
        return new JsonResult(new
        {
            url = $"{Request.Scheme}://{Request.Host}/social/threads/delete-status?id={confirmationCode}",
            confirmation_code = confirmationCode,
        });
    }

    // A generic confirmation page for the URL above. Intentionally stateless — by the time this URL
    // could be visited, the deletion the confirmation_code refers to has already completed (or there
    // was nothing to delete), so there's no per-code status to look up.
    [HttpGet("social/threads/delete-status")]
    public ContentResult DeleteStatus() => Content(
        "<!doctype html><html><body><h1>Deletion complete</h1>" +
        "<p>Any ShortLynx data associated with your Threads account has been deleted.</p></body></html>",
        "text/html");
}
