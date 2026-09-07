namespace TubaWinUi3.Services;

public static class CompactModeService
{
    private const string CompactModeKey = "CompactModeEnabled";

    public static event Action<bool>? CompactModeChanged;

    public static bool IsCompactModeEnabled() => AppSettings.GetBool(CompactModeKey);

    public static void SetCompactModeEnabled(bool enabled)
    {
        AppSettings.Set(CompactModeKey, enabled);
        CompactModeChanged?.Invoke(enabled);
    }
}
