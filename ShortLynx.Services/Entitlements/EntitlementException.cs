namespace ShortLynx.Services.Entitlements;

/// <summary>
/// Thrown when an action is blocked by the account's plan (quota reached or feature not in tier). Never
/// thrown under <see cref="UnlimitedEntitlements"/>; surfaces only when a hosted policy denies. Callers
/// map it to an "upgrade required" response (HTTP 402).
/// </summary>
public sealed class EntitlementException(string message) : Exception(message);
