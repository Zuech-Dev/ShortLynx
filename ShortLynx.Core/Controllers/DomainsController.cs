using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Entities;
using ShortLynx.Services.ApiKeys;
using ShortLynx.Services.Domains;

namespace ShortLynx.Core.Controllers;

[ApiController]
[Route("domains")]
[Authorize(AuthenticationSchemes = ApiKeyAuthHandler.SchemeName)]
public class DomainsController(
    ICustomDomainService domains,
    IOptions<CustomDomainOptions> domainOptions) : ControllerBase
{
    private ApiKeyEntity CurrentKey => (ApiKeyEntity)HttpContext.Items["ApiKey"]!;

    // Custom domains are scoped to a user account, so the API key must be associated with one.
    private IActionResult? RequireUser(out Guid userId)
    {
        if (CurrentKey.UserAccountId is { } uid)
        {
            userId = uid;
            return null;
        }

        userId = default;
        return BadRequest(new
        {
            error = "This API key is not associated with a user account; custom domains are user-scoped."
        });
    }

    // POST /domains
    [HttpPost]
    [RequireScope(Scopes.DomainsWrite)]
    public async Task<IActionResult> Add([FromBody] AddDomainRequest request, CancellationToken ct)
    {
        if (RequireUser(out var userId) is { } err) return err;

        try
        {
            var domain = await domains.AddAsync(request.Domain, userId, ct);
            return CreatedAtAction(nameof(Get), new { id = domain.Id }, ToResponse(domain));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    // GET /domains
    [HttpGet]
    [RequireScope(Scopes.DomainsRead)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        if (RequireUser(out var userId) is { } err) return err;
        var list = await domains.ListAsync(userId, ct);
        return Ok(list.Select(ToResponse));
    }

    // GET /domains/{id}
    [HttpGet("{id:guid}")]
    [RequireScope(Scopes.DomainsRead)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (RequireUser(out var userId) is { } err) return err;
        var domain = (await domains.ListAsync(userId, ct)).FirstOrDefault(d => d.Id == id);
        return domain is null ? NotFound() : Ok(ToResponse(domain));
    }

    // POST /domains/{id}/verify
    [HttpPost("{id:guid}/verify")]
    [RequireScope(Scopes.DomainsWrite)]
    public async Task<IActionResult> Verify(Guid id, CancellationToken ct)
    {
        if (RequireUser(out var userId) is { } err) return err;
        var domain = await domains.VerifyAsync(id, userId, ct);
        return domain is null ? NotFound() : Ok(ToResponse(domain));
    }

    // DELETE /domains/{id}
    [HttpDelete("{id:guid}")]
    [RequireScope(Scopes.DomainsWrite)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
    {
        if (RequireUser(out var userId) is { } err) return err;
        return await domains.RemoveAsync(id, userId, ct) ? NoContent() : NotFound();
    }

    private DomainResponse ToResponse(CustomDomainEntity d) => new(
        d.Id,
        d.Domain,
        d.VerificationStatus.ToString(),
        domainOptions.Value.VerificationHost(d.Domain),
        domainOptions.Value.ExpectedTxtValue(d.VerificationToken),
        d.VerifiedAt,
        d.CreatedAt);
}
