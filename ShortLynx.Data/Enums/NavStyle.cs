namespace ShortLynx.Data.Enums;

/// <summary>
/// A user's preferred navigation layout in the Next.js hosted dashboard (ShortLynx.Hosted), persisted
/// per-user so it follows them across devices. Unrelated to ShortLynx.Admin.Services.NavStyle -- same
/// concept and member names, but that one is a Blazor-only, browser-local (localStorage) preference
/// with its own separate enum in a separate project. The two dashboards are not required to have
/// feature parity or a shared implementation.
/// </summary>
public enum NavStyle
{
    Hamburger = 0,
    HorizontalScroll = 1,
}
