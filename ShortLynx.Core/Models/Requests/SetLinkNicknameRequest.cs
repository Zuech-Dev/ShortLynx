namespace ShortLynx.Core.Models.Requests;

/// <summary>Sets a link's display nickname, or clears it when Nickname is null.</summary>
public sealed record SetLinkNicknameRequest(string? Nickname);
