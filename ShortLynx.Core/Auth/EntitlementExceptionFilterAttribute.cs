using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ShortLynx.Services.Entitlements;

namespace ShortLynx.Core.Auth;

/// <summary>
/// Maps a plan denial (<see cref="EntitlementException"/>) to 402 Payment Required wherever it escapes
/// an action. An attribute on the controller base classes rather than a global MVC filter on purpose: a
/// hosted composition root calls its own <c>AddControllers()</c>, so a filter registered in this app's
/// Program.cs would silently not apply there — the one deployment that actually denies anything.
/// Actions that already catch it themselves keep their own handling; this is the safety net for every
/// gate added after them.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class EntitlementExceptionFilterAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not EntitlementException ex) return;

        context.Result = new ObjectResult(new { error = ex.Message })
        {
            StatusCode = StatusCodes.Status402PaymentRequired,
        };
        context.ExceptionHandled = true;
    }
}
