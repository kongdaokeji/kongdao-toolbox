namespace TubaWinUi3.Services;

public static class NavLayoutModeService
{
    private const string NavLayoutModeKey = "NavLayoutMode";

    public static event Action<string>? NavLayoutModeChanged;

    public static string GetNavLayoutMode() => AppSettings.Get(NavLayoutModeKey) ?? "sidebar";

    public static void SetNavLayoutMode(string mode)
    {
        AppSettings.Set(NavLayoutModeKey, mode);
        NavLayoutModeChanged?.Invoke(mode);
    }

    public static bool IsTabMode() => GetNavLayoutMode() == "tabs";
}
