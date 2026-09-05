namespace ShortLynx.Core.Models.Responses;

/// <summary>A folder in the current account, with how many links are currently filed in it.</summary>
public sealed record FolderResponse(Guid Id, string Name, int LinkCount, DateTimeOffset CreatedAt);
