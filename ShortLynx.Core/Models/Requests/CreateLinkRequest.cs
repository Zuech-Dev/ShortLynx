using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

public sealed record CreateLinkRequest(
    [Required, Url] string Url);
