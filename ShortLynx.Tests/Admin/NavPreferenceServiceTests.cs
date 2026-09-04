using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using ShortLynx.Admin.Services;

namespace ShortLynx.Tests.Admin;

public class NavPreferenceServiceTests : BunitContext
{
    private const string Key = "shortlynx-nav-style";

    private NavPreferenceService NewService() => new(Services.GetRequiredService<IJSRuntime>());

    [Fact]
    public async Task LoadAsync_NoStoredValue_KeepsHamburgerDefault_AndRaisesChanged()
    {
        JSInterop.Setup<string?>("localStorage.getItem", Key).SetResult(null);
        var svc = NewService();
        var changed = 0;
        svc.Changed += () => changed++;

        await svc.LoadAsync();

        Assert.Equal(NavStyle.Hamburger, svc.Style);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task LoadAsync_StoredValue_AppliesIt()
    {
        JSInterop.Setup<string?>("localStorage.getItem", Key).SetResult(nameof(NavStyle.HorizontalScroll));
        var svc = NewService();

        await svc.LoadAsync();

        Assert.Equal(NavStyle.HorizontalScroll, svc.Style);
    }

    [Fact]
    public async Task LoadAsync_GarbageStoredValue_FallsBackToDefault()
    {
        JSInterop.Setup<string?>("localStorage.getItem", Key).SetResult("not-a-real-style");
        var svc = NewService();

        await svc.LoadAsync();

        Assert.Equal(NavStyle.Hamburger, svc.Style);
    }

    [Fact]
    public async Task SetAsync_PersistsToLocalStorage_AndRaisesChanged()
    {
        var invocation = JSInterop.SetupVoid("localStorage.setItem", Key, nameof(NavStyle.HorizontalScroll));
        invocation.SetVoidResult();
        var svc = NewService();
        var changed = 0;
        svc.Changed += () => changed++;

        await svc.SetAsync(NavStyle.HorizontalScroll);

        Assert.Equal(NavStyle.HorizontalScroll, svc.Style);
        Assert.Equal(1, changed);
        invocation.VerifyInvoke("localStorage.setItem");
    }

    [Fact]
    public async Task SetAsync_SameStyleAsCurrent_IsNoOp()
    {
        var svc = NewService(); // starts on Hamburger
        var changed = 0;
        svc.Changed += () => changed++;

        await svc.SetAsync(NavStyle.Hamburger);

        Assert.Equal(0, changed);
    }
}
