namespace ShortLynx.Services.ShortCodes;

/// <summary>
/// Thrown when a requested custom code collides with an existing one at insert time (the authoritative
/// uniqueness check). Distinct from a validation failure — the code is well-formed, just not free.
/// Controllers map this to HTTP 409 Conflict.
/// </summary>
public sealed class CustomCodeTakenException(string code)
    : Exception($"The custom code '{code}' is already taken.")
{
    public string Code { get; } = code;
}
