using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Folders;

/// <summary>Mutable fields of a folder.</summary>
public sealed record FolderInput(string Name);

public interface IFolderService
{
    Task<FolderEntity> CreateAsync(
        Guid accountId, FolderInput input, Guid? createdByUserAccountId = null, CancellationToken ct = default);

    Task<IReadOnlyList<FolderEntity>> ListAsync(Guid accountId, CancellationToken ct = default);

    Task<FolderEntity?> GetAsync(Guid id, Guid accountId, CancellationToken ct = default);

    Task<FolderEntity?> UpdateAsync(Guid id, Guid accountId, FolderInput input, CancellationToken ct = default);

    Task<bool> DeleteAsync(Guid id, Guid accountId, CancellationToken ct = default);
}
