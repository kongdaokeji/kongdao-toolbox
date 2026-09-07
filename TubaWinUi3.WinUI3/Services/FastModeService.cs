namespace TubaWinUi3.Services;

public static class FastModeService
{
    private const string FastModeKey = "FastModeEnabled";

    public static event Action<bool>? FastModeChanged;

    public static bool IsFastModeEnabled() => AppSettings.GetBool(FastModeKey);

    public static void SetFastModeEnabled(bool enabled)
    {
        AppSettings.Set(FastModeKey, enabled);
        FastModeChanged?.Invoke(enabled);
    }
}
