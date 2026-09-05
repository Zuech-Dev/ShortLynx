namespace ShortLynx.Core.Models.Responses;

/// <summary>A tag in the current account, with how many links currently carry it.</summary>
public sealed record TagResponse(Guid Id, string Name, int LinkCount, DateTimeOffset CreatedAt);
