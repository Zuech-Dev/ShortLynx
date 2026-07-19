using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ShortLynx.Core.Auth;

/// <summary>
/// Ensures a session-authenticated request actually carries the identity claims the <c>/me/*</c>
/// surface depends on. The JWT bearer handler only validates the token's signature and expiry, not
/// which claims it contains — so a validly signed token minted without an <c>account_id</c> (e.g. an
/// allowlisted user with no membership, or a hand-crafted token) would otherwise reach
/// <see cref="SessionControllerBase.AccountId"/>'s <c>Guid.Parse</c> and throw a 500. This turns that
/// into a clean 401. Applied to <see cref="SessionControllerBase"/> so it covers every action —
/// reads included, where <see cref="RequireAccountActionAttribute"/> (writes only) doesn't run.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ValidateSessionClaimsAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var user = context.HttpContext.User;
        if (!user.TryUserId(out _) || !user.TryAccountId(out _))
            context.Result = new UnauthorizedObjectResult(new { error = "Session token is missing required claims." });
    }
}
