namespace TubaWinUi3.Services.Agent;

/// <summary>工具层共享小助手。</summary>
internal static class AgentToolHelpers
{
    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double size = bytes;
        var unitIdx = 0;
        while (size >= 1024 && unitIdx < units.Length - 1)
        {
            size /= 1024;
            unitIdx++;
        }
        return $"{size:F1} {units[unitIdx]}";
    }

    public static string Truncate(string text, int max)
        => text.Length <= max ? text : text[..max] + "…";
}
