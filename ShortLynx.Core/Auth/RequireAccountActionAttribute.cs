using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ShortLynx.Services.Accounts;

namespace ShortLynx.Core.Auth;

/// <summary>
/// Gates a session-authenticated (<c>/me/*</c>) endpoint on the actor's role in the acting account,
/// per <see cref="AccountPermissions"/>. The role is resolved from the database on every request —
/// deliberately not from the JWT's role claim, so a demotion takes effect immediately rather than at
/// token expiry, and a forged/stale claim can never widen access. A user who is no longer a member of
/// the account gets 403 regardless of what their token says.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireAccountActionAttribute : ActionFilterAttribute
{
    private readonly AccountAction _action;

    public RequireAccountActionAttribute(AccountAction action)
    {
        _action = action;
        // Run before [ApiController]'s ModelStateInvalidFilter (Order -2000) so an under-privileged
        // caller always sees 403 — never a 400 that confirms whether their body parsed.
        Order = -3000;
    }

    public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var user = context.HttpContext.User;
        if (!user.TryUserId(out var userId) || !user.TryAccountId(out var accountId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var accounts = context.HttpContext.RequestServices.GetRequiredService<IAccountService>();
        var role = await accounts.GetRoleAsync(accountId, userId, context.HttpContext.RequestAborted);

        if (role is not { } r || !AccountPermissions.Can(r, _action))
        {
            context.Result = new ObjectResult(new { error = "Your role in this account doesn't permit this action." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
            return;
        }

        await next();
    }
}
