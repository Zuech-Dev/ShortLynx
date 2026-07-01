namespace ShortLynx.Data.Enums;

/// <summary>
/// The platform a click came from, derived at write time from the request's Referer header.
/// Stored on each visit so "clicks by platform" is a cheap GROUP BY rather than a read-time re-parse.
/// </summary>
public enum ClickSource
{
    /// <summary>No referrer — typed/bookmarked, a QR scan, an email client, or an app that strips it.</summary>
    Direct = 0,

    /// <summary>A referrer was present but matched no known platform.</summary>
    Other = 1,

    Twitter = 2,
    Bluesky = 3,
    Mastodon = 4,
    LinkedIn = 5,
    Reddit = 6,
    Facebook = 7,
    Instagram = 8,
    Threads = 9,
}
