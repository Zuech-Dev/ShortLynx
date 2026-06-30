namespace ShortLynx.Admin;

/// <summary>
/// Marker type identifying the Admin assembly for <c>WebApplicationFactory</c> in tests.
/// Used instead of the global <c>Program</c> type, which would clash with the Core app's
/// top-level-statement <c>Program</c> once both projects are referenced by the test assembly.
/// </summary>
public sealed class AdminEntryPoint;
