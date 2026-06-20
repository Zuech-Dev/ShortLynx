namespace ShortLynx.Services.ApiKeys;

/// <summary>
/// API-key authorization scopes. Shared by Core (endpoint enforcement), Admin (key-creation UI),
/// and tests, so it lives in Services rather than Core.
/// </summary>
public static class Scopes
{
    public const string LinksRead = "links:read";
    public const string LinksWrite = "links:write";
    public const string CodesWrite = "codes:write";
    public const string AnalyticsRead = "analytics:read";

    /// <summary>All known scopes, for UI listing.</summary>
    public static readonly string[] All = [LinksRead, LinksWrite, CodesWrite, AnalyticsRead];
}
