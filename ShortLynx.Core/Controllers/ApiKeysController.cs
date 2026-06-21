using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using ShortLynx.Core.Models.Requests;
using ShortLynx.Core.Models.Responses;
using ShortLynx.Core.RateLimit;
using ShortLynx.Data.Entities;
using ShortLynx.Services.ApiKeys;

namespace ShortLynx.Core.Controllers;

[ApiController]
[Route("api-keys")]
[EnableRateLimiting(RateLimitPolicies.ApiKeys)]
public class ApiKeysController(IApiKeyService apiKeyService, IOptions<ApiKeyOptions> options) : ControllerBase
{
    /// <summary>
    /// Provisions a new API key. Requires the server's AdminSecret in the Authorization header.
    /// Returns the plaintext key once — it is never stored and cannot be retrieved again.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateApiKey(
        [FromBody] CreateApiKeyRequest request,
        CancellationToken ct)
    {
        var adminSecret = options.Value.AdminSecret;
        if (string.IsNullOrWhiteSpace(adminSecret))
            return StatusCode(503, new { error = "API key provisioning is not configured on this server." });

        var authHeader = HttpContext.Request.Headers.Authorization.ToString();
        if (!authHeader.StartsWith("Bearer ", StringComparison.Ordinal))
            return Unauthorized(new { error = "Admin secret required." });

        var provided = authHeader["Bearer ".Length..].Trim();
        if (!ConstantTimeEquals(provided, adminSecret))
            return Unauthorized(new { error = "Invalid admin secret." });

        ApiKeyEntity record;
        string plaintext;
        try
        {
            (record, plaintext) = await apiKeyService.CreateAsync(
                request.Name, request.Scopes ?? [], request.AccountId, ct: ct);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        return Ok(new ApiKeyResponse(
            record.Id,
            record.Name,
            plaintext,
            record.Scopes.Split(',', StringSplitOptions.RemoveEmptyEntries),
            record.CreatedAt));
    }

    // Hash both sides before comparing to normalize length and maintain constant time.
    private static bool ConstantTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            SHA256.HashData(Encoding.UTF8.GetBytes(a)),
            SHA256.HashData(Encoding.UTF8.GetBytes(b)));
}
