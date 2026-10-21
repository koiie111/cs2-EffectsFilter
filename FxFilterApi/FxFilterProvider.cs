namespace FxFilterApi;

/// <summary>
/// CSS keeps the first capability supplier after unloading its plugin. Register this
/// shared method once so reloads replace the instance without retaining the plugin.
/// All access occurs on the game thread.
/// </summary>
public static class FxFilterProvider
{
    private static IFxFilter? _current;
    private static bool _registered;
    public static bool Attach(IFxFilter service)
    {
        _current = service;
        if (_registered) return false;
        _registered = true;
        return true;
    }
    public static void Detach(IFxFilter service)
    {
        if (ReferenceEquals(_current, service)) _current = null;
    }
    public static IFxFilter Get() => _current ?? Empty.Instance;
    private sealed class Empty : IFxFilter, IFxServerSettings
    {
        public static readonly Empty Instance = new();
        public bool IsHiddenFor(int viewerSlot, FxCategory category) => false;
        public bool IsDisabledOnServer(FxCategory category) => false;
        public bool RainbowDisabled => false;
    }
}
