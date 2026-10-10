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
    /// <summary>
    /// Browser-looking User-Agent that sent no <c>Sec-Fetch-Site</c> header on an HTTPS request. Every
    /// current browser sends it on a top-level navigation to a secure origin, so this is almost always a link scanner (carrier/security filters on
    /// SMS and email) dressed up as Chrome or Safari. Never assigned to privacy-signal clicks, which
    /// carry no derived dimensions at all. Counted as automated, not human, in every aggregate.
    /// </summary>
    SuspectedAutomated = 5,
}
