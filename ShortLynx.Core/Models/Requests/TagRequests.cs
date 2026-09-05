using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

/// <summary>Create a tag in the current account. Names are unique per account (case-insensitive).</summary>
public sealed record CreateTagRequest([Required] string Name);

/// <summary>Rename a tag.</summary>
public sealed record UpdateTagRequest([Required] string Name);

/// <summary>Full-replace the set of tags on a link.</summary>
public sealed record SetLinkTagsRequest(Guid[] TagIds);
