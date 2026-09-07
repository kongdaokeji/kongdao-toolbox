namespace TubaWinUi3.Services.Agent;

/// <summary>
/// Agent 运行时调试日志（写入 DataDir/agent-debug.log），
/// 用于定位确认流/多轮循环在真实环境中的卡点。
/// </summary>
internal static class AgentDebugLog
{
    private static readonly object Lock = new();
    private static string? _path;

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception? ex = null)
        => Write("ERROR", message + (ex is null ? "" : $" | {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}"));

    private static void Write(string level, string message)
    {
        try
        {
            _path ??= Path.Combine(ConfigManager.GetDataDir(), "agent-debug.log");
            lock (Lock)
            {
                File.AppendAllText(_path,
                    $"[{DateTime.Now:HH:mm:ss.fff}] {level} {message}\n");
            }
        }
        catch { }
    }
}
