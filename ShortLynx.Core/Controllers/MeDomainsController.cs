using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Accounts;
using ShortLynx.Services.Domains;

namespace ShortLynx.Core.Controllers;

[Route("me/domains")]
public class MeDomainsController(
    ICustomDomainService domains,
    IOptions<CustomDomainOptions> domainOptions) : SessionControllerBase
{
    // GET /me/domains
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var list = await domains.ListAsync(AccountId, ct);
        return Ok(list.Select(ToResponse));
    }

    // POST /me/domains
    [HttpPost]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Add([FromBody] AddDomainRequest request, CancellationToken ct)
    {
        try
        {
            var domain = await domains.AddAsync(request.Domain, AccountId, CurrentUserId, ct);
            return CreatedAtAction(nameof(Get), new { id = domain.Id }, ToResponse(domain));
        }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    // GET /me/domains/{id}
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var domain = (await domains.ListAsync(AccountId, ct)).FirstOrDefault(d => d.Id == id);
        return domain is null ? NotFound() : Ok(ToResponse(domain));
    }

    // POST /me/domains/{id}/verify
    [HttpPost("{id:guid}/verify")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Verify(Guid id, CancellationToken ct)
    {
        var domain = await domains.VerifyAsync(id, AccountId, ct);
        return domain is null ? NotFound() : Ok(ToResponse(domain));
    }

    // DELETE /me/domains/{id}
    [HttpDelete("{id:guid}")]
    [RequireAccountAction(AccountAction.ManageResources)]
    public async Task<IActionResult> Remove(Guid id, CancellationToken ct)
        => await domains.RemoveAsync(id, AccountId, ct) ? NoContent() : NotFound();

    private DomainResponse ToResponse(CustomDomainEntity d) => new(
        d.Id, d.Domain, d.VerificationStatus.ToString(),
        domainOptions.Value.VerificationHost(d.Domain),
        domainOptions.Value.ExpectedTxtValue(d.VerificationToken),
        d.VerifiedAt, d.CreatedAt);
}
