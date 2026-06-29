using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

public sealed record CreateUserCodesRequest(
    [Required, MinLength(1)] Guid[] UserIds);
