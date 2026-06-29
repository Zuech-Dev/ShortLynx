namespace ShortLynx.Services.ApiKeys;

public class ApiKeyOptions
{
    /// <summary>The shipped placeholder; startup validation rejects it so a real secret must be set.</summary>
    public const string DefaultPlaceholderSecret = "CHANGE-ME-use-a-32+-char-secret-in-production";

    public required string HmacSecret { get; set; }

    /// <summary>
    /// If set, POST /api-keys requires "Authorization: Bearer {AdminSecret}" to provision new keys.
    /// Leave null/empty to disable the provisioning endpoint entirely.
    /// </summary>
    public string? AdminSecret { get; set; }
}
