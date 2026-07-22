using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

/// <summary><c>CustomCode</c> requests an operator-chosen vanity code (paid on the hosted service).</summary>
public sealed record CreateLinkRequest(
    [Required, Url] string Url,
    string? CustomCode = null);
