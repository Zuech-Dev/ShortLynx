using ShortLynx.Data.Enums;

namespace ShortLynx.Admin.Components;

/// <summary>One click as shown in the filterable clicks table — no hashed IP (display is identity-free).</summary>
public sealed record ClickRow(DateTimeOffset ClickedAt, ClickSource Source, DeviceType Device, string? ReferrerHost);
