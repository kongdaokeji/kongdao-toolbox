namespace TubaWinUi3.Services;

public static class FastModeService
{
    // “低配模式”：在“快速模式”（禁用动画）基础上，额外跳过启动期后台杂活
    // （图标缓存清理、硬件 WMI 预热、空白分类清理、更新检查），降低老电脑资源占用。
    // 保留 FastModeEnabled 键名以兼容既有用户设置与导航映射。
    private const string FastModeKey = "FastModeEnabled";

    public static bool IsFastModeEnabled() => AppSettings.GetBool(FastModeKey);

    public static void SetFastModeEnabled(bool enabled)
    {
        AppSettings.Set(FastModeKey, enabled);
    }
}
