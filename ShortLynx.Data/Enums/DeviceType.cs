namespace ShortLynx.Data.Enums;

/// <summary>
/// Coarse device class derived at write time from the request's User-Agent. Low cardinality on purpose:
/// the campaign-relevant question is "mobile vs. desktop" (does the audience favour QR codes?), not an
/// exact device. No fingerprinting — this is a single enum bucket, not a device signature.
/// </summary>
public enum DeviceType
{
    /// <summary>No User-Agent, or one we couldn't classify.</summary>
    Unknown = 0,
    Desktop = 1,
    Mobile = 2,
    Tablet = 3,
    /// <summary>Automated client (crawler, link-preview fetcher, scanner).</summary>
    Bot = 4,
}
