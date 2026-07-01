using ShortLynx.Data.Enums;

namespace ShortLynx.Admin.Components;

/// <summary>One click as shown in the filterable clicks table — no hashed IP (display is identity-free).</summary>
public sealed record ClickRow(
    DateTimeOffset ClickedAt,
    ClickSource Source,
    DeviceType Device,
    string? ReferrerHost,
    string? Browser = null,
    string? Os = null,
    string? Country = null,
    string? Language = null);
