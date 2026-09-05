using Microsoft.EntityFrameworkCore;
using ShortLynx.Data.Context;
using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Tags;

public sealed class TagService(ShortLynxDbContext db) : ITagService
{
    public async Task<TagEntity> CreateAsync(Guid accountId, TagInput input, CancellationToken ct = default)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ArgumentException("Enter a tag name.", nameof(input));

        if (await db.TagEntities.AnyAsync(t => t.AccountId == accountId && t.Name.ToLower() == name.ToLower(), ct))
            throw new InvalidOperationException($"A tag named '{name}' already exists.");

        var entity = new TagEntity
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.TagEntities.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<TagEntity>> ListAsync(Guid accountId, CancellationToken ct = default)
        => await db.TagEntities
            .Where(t => t.AccountId == accountId)
            .OrderBy(t => t.Name)
            .ToListAsync(ct);

    public Task<TagEntity?> GetAsync(Guid id, Guid accountId, CancellationToken ct = default)
        => db.TagEntities.FirstOrDefaultAsync(t => t.Id == id && t.AccountId == accountId, ct);

    public async Task<TagEntity?> UpdateAsync(Guid id, Guid accountId, TagInput input, CancellationToken ct = default)
    {
        var name = (input.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            throw new ArgumentException("Enter a tag name.", nameof(input));

        var tag = await db.TagEntities.FirstOrDefaultAsync(t => t.Id == id && t.AccountId == accountId, ct);
        if (tag is null) return null;

        if (await db.TagEntities.AnyAsync(
                t => t.AccountId == accountId && t.Id != id && t.Name.ToLower() == name.ToLower(), ct))
            throw new InvalidOperationException($"A tag named '{name}' already exists.");

        tag.Name = name;
        await db.SaveChangesAsync(ct);
        return tag;
    }

    public async Task<bool> DeleteAsync(Guid id, Guid accountId, CancellationToken ct = default)
        // LinkTagEntity rows for this tag cascade-delete with it (Configuration.cs), so unlike
        // Folder/Campaign there's no unassign-first step -- a join row has no orphaned-but-visible state.
        => await db.TagEntities.Where(t => t.Id == id && t.AccountId == accountId).ExecuteDeleteAsync(ct) > 0;

    public async Task<bool> SetLinkTagsAsync(
        Guid linkId, IReadOnlyCollection<Guid> tagIds, Guid accountId, CancellationToken ct = default)
    {
        if (!await db.LinkEntities.AnyAsync(l => l.Id == linkId && l.AccountId == accountId, ct))
            return false;

        var distinctTagIds = tagIds.Distinct().ToList();
        if (distinctTagIds.Count > 0)
        {
            var ownedCount = await db.TagEntities
                .CountAsync(t => t.AccountId == accountId && distinctTagIds.Contains(t.Id), ct);
            if (ownedCount != distinctTagIds.Count) return false;
        }

        await db.LinkTagEntities.Where(lt => lt.LinkId == linkId).ExecuteDeleteAsync(ct);
        if (distinctTagIds.Count > 0)
        {
            db.LinkTagEntities.AddRange(distinctTagIds.Select(tagId => new LinkTagEntity
            {
                Id = Guid.CreateVersion7(),
                LinkId = linkId,
                TagId = tagId,
                CreatedAt = DateTimeOffset.UtcNow,
            }));
        }
        await db.SaveChangesAsync(ct);
        return true;
    }
}
