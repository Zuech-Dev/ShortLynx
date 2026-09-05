using Microsoft.AspNetCore.Mvc;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Enums;
using ShortLynx.Services.Users;

namespace ShortLynx.Core.Controllers;

/// <summary>
/// GET/PUT /me/preferences — a signed-in user's own preferences. Deliberately NOT gated by
/// [RequireAccountAction]: unlike /me/account this isn't scoped to the acting account or the caller's
/// role in it at all -- a Viewer sets their own nav style the same as an Owner. CurrentUserId is the
/// only key. See RoleEnforcementTests for the test pinning this down as intentional, not an oversight.
/// </summary>
[Route("me/preferences")]
public class MePreferencesController(IUserPreferencesService preferences) : SessionControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var style = await preferences.GetNavStyleAsync(CurrentUserId, ct);
        return style is null ? Unauthorized() : Ok(new UserPreferencesResponse(style.Value.ToString()));
    }

    [HttpPut]
    public async Task<IActionResult> Update([FromBody] UpdatePreferencesRequest request, CancellationToken ct)
    {
        if (!Enum.TryParse<NavStyle>(request.NavStyle, ignoreCase: true, out var style) || !Enum.IsDefined(style))
            return BadRequest(new { error = $"Unknown nav style '{request.NavStyle}'." });

        return await preferences.SetNavStyleAsync(CurrentUserId, style, ct)
            ? Ok(new UserPreferencesResponse(style.ToString()))
            : Unauthorized();
    }
}
