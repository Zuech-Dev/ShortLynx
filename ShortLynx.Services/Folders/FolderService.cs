using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Folders;

public sealed class FolderService(ShortLynxDbContext db) : IFolderService
{
    public async Task<FolderEntity> CreateAsync(
        Guid accountId, FolderInput input, Guid? createdByUserAccountId = null, CancellationToken ct = default)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ArgumentException("Enter a folder name.", nameof(input));

        var entity = new FolderEntity
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            UserAccountId = createdByUserAccountId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.FolderEntities.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<FolderEntity>> ListAsync(Guid accountId, CancellationToken ct = default)
        => await db.FolderEntities
            .Where(f => f.AccountId == accountId)
            .OrderByDescending(f => f.Id)
            .ToListAsync(ct);

    public Task<FolderEntity?> GetAsync(Guid id, Guid accountId, CancellationToken ct = default)
        => db.FolderEntities.FirstOrDefaultAsync(f => f.Id == id && f.AccountId == accountId, ct);

    public async Task<FolderEntity?> UpdateAsync(
        Guid id, Guid accountId, FolderInput input, CancellationToken ct = default)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ArgumentException("Enter a folder name.", nameof(input));

        var folder = await db.FolderEntities.FirstOrDefaultAsync(f => f.Id == id && f.AccountId == accountId, ct);
        if (folder is null) return null;

        folder.Name = name;
        await db.SaveChangesAsync(ct);
        return folder;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid accountId, CancellationToken ct = default)
    {
        // Links keep existing; the FK is configured SetNull, but we don't rely on cascade timing here —
        // unassign first so the operation is provider-agnostic, then delete the folder.
        await db.LinkEntities
            .Where(l => l.FolderId == id && l.AccountId == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(l => l.FolderId, (Guid?)null), ct);

        var affected = await db.FolderEntities
            .Where(f => f.Id == id && f.AccountId == accountId)
            .ExecuteDeleteAsync(ct);
        return affected > 0;
    }
}
