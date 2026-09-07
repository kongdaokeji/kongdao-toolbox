namespace TubaWinUI3.BackEnd;

/// <summary>轻量日志：控制台 + 可选文件。线程安全。</summary>
public static class BackEndLog
{
    private static readonly object _lock = new();
    private static string? _file;

    public static void Configure(string? logFile)
    {
        lock (_lock)
        {
            _file = string.IsNullOrWhiteSpace(logFile) ? null : logFile;
            if (_file is not null)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_file);
                    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                }
                catch { }
            }
        }
    }

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        lock (_lock)
        {
            try { Console.WriteLine(line); } catch { }
            if (_file is not null)
            {
                try
                {
                    var dir = Path.GetDirectoryName(_file);
                    if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
                    File.AppendAllText(_file, line + Environment.NewLine);
                }
                catch { }
            }
        }
    }
}
