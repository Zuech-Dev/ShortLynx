using System.ComponentModel.DataAnnotations.Schema;
using ShortLynx.Data.Enums;

namespace ShortLynx.Data.Entities;

/// <summary>
/// A post published to a social platform for one of the account's links. Ties the platform's post back
/// to the link (and, via the link, its campaign) so pulled metrics (impressions/likes) sit beside our
/// click data → CTR = clicks ÷ impressions. Platform + handle are denormalized and the connection FK is
/// SetNull, so the posting history survives a later disconnect.
/// </summary>
[Table("SocialPosts")]
public class SocialPostEntity
{
    public Guid Id { get; set; }

    /// <summary>The owning account. Posts scope by AccountId.</summary>
    public Guid AccountId { get; set; }

    /// <summary>The link the post promotes.</summary>
    public Guid LinkId { get; set; }

    /// <summary>The connection used to publish; null after that connection is removed.</summary>
    public Guid? SocialConnectionId { get; set; }

    public SocialPlatform Platform { get; set; }
    public required string Handle { get; set; }

    /// <summary>The platform's post id (Bluesky: the at:// record URI; Mastodon: the status id).</summary>
    public required string ExternalPostId { get; set; }

    /// <summary>Human-viewable URL of the post, when the platform exposes one.</summary>
    public string? PostUrl { get; set; }

    public required string Text { get; set; }
    public DateTimeOffset PostedAt { get; set; }

    // Pulled metrics — null until the first successful metrics fetch (next slice).
    public long? Impressions { get; set; }
    public long? Likes { get; set; }
    public long? Reposts { get; set; }
    public long? Replies { get; set; }
    public DateTimeOffset? MetricsUpdatedAt { get; set; }

    public virtual AccountEntity Account { get; set; } = null!;
    public virtual LinkEntity Link { get; set; } = null!;
    public virtual SocialConnectionEntity? SocialConnection { get; set; }

    /// <summary>
    /// This post's own attribution code, if one was minted (see <see cref="SocialPostCodeEntity"/>).
    /// Absent when the author inlined their own short URL — those posts fall back to referrer-based
    /// <c>ClickSource</c> attribution.
    /// </summary>
    public virtual SocialPostCodeEntity? SocialPostCode { get; set; }
}
