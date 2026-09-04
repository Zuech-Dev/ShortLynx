using Microsoft.AspNetCore.Mvc;
using ShortLynx.Core.Auth;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Data.Entities;
using ShortLynx.Services.Accounts;

namespace ShortLynx.Core.Controllers;

[Route("me/account")]
public class MeAccountController(IAccountService accounts) : SessionControllerBase
{
    // GET /me/account — any member can view the current account's own settings.
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var account = await accounts.GetAccountAsync(AccountId, ct);
        return account is null ? NotFound() : Ok(ToResponse(account));
    }

    // PUT /me/account — rename the account and set the privacy/terms URLs the Mode 2 disclosure
    // interstitial reads. Owner-only, matching ManageAccount's documented "rename... the account" scope.
    [HttpPut]
    [RequireAccountAction(AccountAction.ManageAccount)]
    public async Task<IActionResult> Update([FromBody] UpdateAccountRequest request, CancellationToken ct)
    {
        try
        {
            var account = await accounts.UpdateAccountAsync(
                AccountId, request.Name, request.PrivacyPolicyUrl, request.TermsOfServiceUrl,
                request.ConfirmsDisclosure, request.EnableCityAggregates, ct);
            return account is null ? NotFound() : Ok(ToResponse(account));
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static AccountSettingsResponse ToResponse(AccountEntity a)
        => new(a.Id, a.Name, a.PrivacyPolicyUrl, a.TermsOfServiceUrl, a.EnableCityAggregates);
}
