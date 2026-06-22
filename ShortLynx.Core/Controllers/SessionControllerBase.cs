using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortLynx.Core.Auth;

namespace ShortLynx.Core.Controllers;

/// <summary>Base for the session-authenticated <c>/me/*</c> surface — scoped to the JWT's account.</summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
public abstract class SessionControllerBase : ControllerBase
{
    protected Guid AccountId => User.AccountId();
    protected Guid CurrentUserId => User.UserId();
}
