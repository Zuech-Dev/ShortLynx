using System.ComponentModel.DataAnnotations;

namespace ShortLynx.Core.Models.Requests;

/// <summary>Create a folder in the current account. Pure organization -- no UTM template.</summary>
public sealed record CreateFolderRequest([Required] string Name);

/// <summary>Rename a folder.</summary>
public sealed record UpdateFolderRequest([Required] string Name);

/// <summary>Files a link into a folder, or removes it from any folder when FolderId is null.</summary>
public sealed record SetLinkFolderRequest(Guid? FolderId);
