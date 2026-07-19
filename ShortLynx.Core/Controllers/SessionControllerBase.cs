using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ShortLynx.Core.Auth;

namespace ShortLynx.Core.Controllers;

/// <summary>Base for the session-authenticated <c>/me/*</c> surface — scoped to the JWT's account.</summary>
[ApiController]
[Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
[ValidateSessionClaims] // 401 (not 500) if the token lacks user/account claims — the getters below then parse safely.
public abstract class SessionControllerBase : ControllerBase
{
    protected Guid AccountId => User.AccountId();
    protected Guid CurrentUserId => User.UserId();
}
