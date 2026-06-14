using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

public sealed record CreateApiKeyRequest(
    [Required, MinLength(1)] string Name,
    string[] Scopes,
    Guid? UserAccountId = null);
