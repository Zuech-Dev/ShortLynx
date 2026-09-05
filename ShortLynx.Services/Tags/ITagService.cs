using ShortLynx.Data.Entities;

namespace ShortLynx.Services.Tags;

/// <summary>Mutable fields of a tag.</summary>
public sealed record TagInput(string Name);

public interface ITagService
{
    /// <summary>Throws <see cref="InvalidOperationException"/> if the account already has a tag with this name.</summary>
    Task<TagEntity> CreateAsync(
        Guid accountId, TagInput input, CancellationToken ct = default);

    Task<IReadOnlyList<TagEntity>> ListAsync(Guid accountId, CancellationToken ct = default);

    Task<TagEntity?> GetAsync(Guid id, Guid accountId, CancellationToken ct = default);

    /// <summary>Throws <see cref="InvalidOperationException"/> if the new name collides with another tag.</summary>
    Task<TagEntity?> UpdateAsync(Guid id, Guid accountId, TagInput input, CancellationToken ct = default);

    /// <summary>Deletes the tag; its <see cref="LinkTagEntity"/> rows cascade-delete with it.</summary>
    Task<bool> DeleteAsync(Guid id, Guid accountId, CancellationToken ct = default);

    /// <summary>
    /// Full-replace: the link ends up tagged with exactly <paramref name="tagIds"/>, no more, no less.
    /// Returns false if the link isn't the account's, or any tag id isn't the account's.
    /// </summary>
    Task<bool> SetLinkTagsAsync(
        Guid linkId, IReadOnlyCollection<Guid> tagIds, Guid accountId, CancellationToken ct = default);
}
