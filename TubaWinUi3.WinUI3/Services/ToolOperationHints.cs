namespace TubaWinUi3.Services;

internal static class ToolOperationHints
{
    public static string For(string name, string category, string? builtinId)
    {
        if (builtinId is "stress-test" or "volume-shader-test" || category.Contains("烤鸡") ||
            new[] { "prime95", "LinX", "FurMark" }.Any(n => name.Contains(n, StringComparison.OrdinalIgnoreCase)))
            return ToolExperience.L("HighLoad", "高负载测试 · 注意温度与散热");
        if (category.Contains("分区") || category.Contains("格式化"))
            return ToolExperience.L("DiskChanges", "可能修改磁盘 · 操作前备份数据");
        if (builtinId is "hosts-editor" or "network-optimize" or "environment-variables" or "hardware-spoofer")
            return ToolExperience.L("SystemChanges", "系统设置修改 · 请核对影响与恢复方式");
        return "";
    }
}
