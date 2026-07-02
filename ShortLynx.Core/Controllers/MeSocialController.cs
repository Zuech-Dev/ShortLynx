using Microsoft.AspNetCore.Mvc;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Entities;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Entitlements;
using ShortLynx.Services.Social;

namespace ShortLynx.Core.Controllers;

[Route("me/social")]
public class MeSocialController(ISocialConnectionService social) : SessionControllerBase
{
    // GET /me/social — the account's connected social accounts (no tokens, ever).
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var connections = await social.ListAsync(AccountId, ct);
        return Ok(connections.Select(ToResponse));
    }

    // POST /me/social — validate credentials against the platform and store the connection.
    [HttpPost]
    public async Task<IActionResult> Connect([FromBody] ConnectSocialRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<SocialPlatform>(request.Platform, ignoreCase: true, out var platform))
            return BadRequest(new { error = $"Unknown platform '{request.Platform}'. Use 'Bluesky' or 'Mastodon'." });

        try
        {
            var connection = await social.ConnectAsync(
                AccountId, CurrentUserId, platform,
                new SocialCredentials(request.Identifier, request.Secret, request.InstanceUrl), ct);
            return CreatedAtAction(nameof(List), null, ToResponse(connection));
        }
        catch (ArgumentException ex)
        {
            // Rejected credentials or an unsupported platform.
            return BadRequest(new { error = ex.Message });
        }
        catch (EntitlementException ex)
        {
            return StatusCode(StatusCodes.Status402PaymentRequired, new { error = ex.Message });
        }
        catch (HttpRequestException)
        {
            // The platform itself was unreachable/unhappy — not the caller's fault.
            return StatusCode(StatusCodes.Status502BadGateway,
                new { error = "The platform could not be reached. Try again shortly." });
        }
    }

    // DELETE /me/social/{id} — disconnect (destroys the stored tokens with the row).
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Disconnect(Guid id, CancellationToken ct)
        => await social.DisconnectAsync(id, AccountId, ct) ? NoContent() : NotFound();

    private static SocialConnectionResponse ToResponse(SocialConnectionEntity c) => new(
        c.Id, c.Platform.ToString(), c.Handle, c.InstanceUrl, c.ExpiresAt, c.CreatedAt);
}
