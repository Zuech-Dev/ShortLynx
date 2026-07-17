using System.ComponentModel.DataAnnotations.Schema;

namespace ShortLynx.Data.Entities;

/// <summary>
/// A short code minted for one published social post, so that post's clicks attribute exactly rather
/// than being guessed from a referrer — which can't tell two posts on the same platform apart, is
/// stripped by most apps, and is suppressed entirely under DNT/GPC. Same shape as
/// <see cref="UserLinkCodeEntity"/> (many codes per link, each identifying one distribution target);
/// <see cref="ShortCodeEntity"/> stays one-per-link for the link's own shared URL.
///
/// Why a code and not a <c>?p=</c> query param: on platforms where links aren't tappable (Instagram
/// captions), people copy or retype the URL and a param is exactly what gets dropped. The code IS the
/// URL, so it survives.
///
/// Minted before publishing (the code has to exist to go in the post text), then pointed at the post
/// once the platform accepts it — so <see cref="SocialPostId"/> is briefly null by design, and stays
/// null only for a code whose publish failed (those get cleaned up).
/// </summary>
[Table("SocialPostCodes")]
public class SocialPostCodeEntity
{
    public Guid Id { get; set; }

    public Guid LinkId { get; set; }

    /// <summary>The post this code was minted for; null between minting and a successful publish.</summary>
    public Guid? SocialPostId { get; set; }

    /// <summary>The public code, unique across this table (see the redirect resolution order).</summary>
    public required string Code { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public bool IsActive { get; set; }

    public virtual LinkEntity Link { get; set; } = null!;
    public virtual SocialPostEntity? SocialPost { get; set; }
}
