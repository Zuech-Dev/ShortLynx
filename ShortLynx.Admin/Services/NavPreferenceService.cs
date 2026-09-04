using Microsoft.JSInterop;

namespace ShortLynx.Admin.Services;

public enum NavStyle
{
    Hamburger,
    HorizontalScroll,
}

// A per-browser UI preference, not an account-wide setting — stored in localStorage rather than the
// database, so it follows the device rather than the account (like a sidebar-collapsed or dark-mode
// toggle would). Registered Scoped, so each connected circuit gets its own copy in memory.
public sealed class NavPreferenceService(IJSRuntime js)
{
    private const string StorageKey = "shortlynx-nav-style";

    public NavStyle Style { get; private set; } = NavStyle.Hamburger;

    public event Action? Changed;

    // Reads the stored preference, if any. Must run after the circuit's first render (JS interop
    // isn't available during prerendering), so callers invoke this from OnAfterRenderAsync(firstRender)
    // — meaning the very first paint always shows the Hamburger default, swapping styles a moment
    // later if the stored preference differs. That flash is an accepted trade-off of a client-stored
    // preference in a server-rendered app.
    public async Task LoadAsync()
    {
        var stored = await js.InvokeAsync<string?>("localStorage.getItem", StorageKey);
        if (Enum.TryParse<NavStyle>(stored, out var style) && Enum.IsDefined(style))
            Style = style;
        Changed?.Invoke();
    }

    public async Task SetAsync(NavStyle style)
    {
        if (style == Style) return;
        Style = style;
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, style.ToString());
        Changed?.Invoke();
    }
}
