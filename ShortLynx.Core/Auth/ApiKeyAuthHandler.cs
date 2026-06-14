using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using ShortLynx.Services.ApiKeys;

namespace ShortLynx.Core.Auth;

public sealed class ApiKeyAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var headerValue))
            return AuthenticateResult.NoResult();

        var raw = headerValue.ToString();
        if (!raw.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var token = raw["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
            return AuthenticateResult.Fail("Empty bearer token.");

        var apiKeySvc = Context.RequestServices.GetRequiredService<IApiKeyService>();
        var entity = await apiKeySvc.ValidateAsync(token);

        if (entity is null)
            return AuthenticateResult.Fail("Invalid or expired API key.");

        Context.Items["ApiKey"] = entity;

        var claims = new[] { new Claim(ClaimTypes.Name, entity.Id.ToString()) };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);
        return AuthenticateResult.Success(ticket);
    }
}
