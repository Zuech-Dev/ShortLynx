using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ShortLynx.Data.Entities;

namespace ShortLynx.Core.Auth;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class RequireScopeAttribute(string scope) : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.HttpContext.Items["ApiKey"] is not ApiKeyEntity key)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var scopes = key.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (!scopes.Contains(scope, StringComparer.Ordinal))
        {
            context.Result = new ObjectResult(new { error = $"This API key lacks the '{scope}' scope." })
            {
                StatusCode = StatusCodes.Status403Forbidden
            };
        }
    }
}
