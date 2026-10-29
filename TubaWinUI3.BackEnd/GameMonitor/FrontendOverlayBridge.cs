using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TubaWinUI3.BackEnd.GameMonitor;

/// <summary>
/// 后端 → 前端覆盖层桥（2026-09-11 重构）：
/// 后端不再自绘原生覆盖层窗口，只负责「检测到游戏」这一件事；
/// 命中时把状态写入数据目录下的 game_overlay_signal.json 并确保主程序在运行，
/// 由主程序（TubaWinUi3.exe）内的 GameOverlayAutoService 轮询该文件，
/// 用主程序现成的 GameOverlayWindow 显示悬浮窗。
///
/// 信号文件为唯一通信媒介（原子写：临时文件 + 覆盖移动），
/// 并带 Utc 时间戳 —— 前端据此识别后端意外退出（信号过期自动关闭覆盖层）。
/// </summary>
internal sealed class FrontendOverlayBridge
{
    /// <summary>主程序进程名（不带 .exe，与 GameProcessFilter 排除名单一致）。</summary>
    public const string FrontendProcessName = "TubaWinUi3";

    private const string FrontendExeName = "TubaWinUi3.exe";
    private const string SignalFileName = "game_overlay_signal.json";

    private readonly string _signalPath;
    private readonly string _frontendExePath;
    private bool _lastActive; // 状态机：避免每 tick 重复写文件
    private (IntPtr Hwnd, uint Pid, string Proc)? _lastGame; // 心跳复用的最近一次命中

    public FrontendOverlayBridge(string dataDir)
    {
        _signalPath = Path.Combine(dataDir, SignalFileName);
        // 主程序与后端同目录部署（参考主程序定位后端 exe 的规则）
        _frontendExePath = Path.Combine(AppContext.BaseDirectory, FrontendExeName);
    }

    public static string SignalFilePathFor(string dataDir) => Path.Combine(dataDir, SignalFileName);

    /// <summary>检测到游戏（已确认）：写信号 + 确保主程序在运行。</summary>
    public void NotifyGame(IntPtr hwnd, uint pid, string proc)
    {
        _lastGame = (hwnd, pid, proc);
        EnsureFrontendRunning();
        WriteSignal(new OverlaySignal
        {
            Active = true,
            Process = proc,
            Pid = pid,
            Hwnd = hwnd.ToInt64(),
            Utc = DateTimeOffset.UtcNow.ToString("o"),
        });
    }

    /// <summary>
    /// 游戏持续在前台时的心跳：刷新信号时间戳（WriteSignal 内部有 6s 节流），
    /// 前端据此判断后端还活着 —— 否则信号 60s 过期，前端会误关悬浮窗
    /// （2026-09-11 实测踩坑：NotifyGame 只在窗口切换时调用，游戏一直前台时心跳从未跑过）。
    /// </summary>
    public void KeepAlive()
    {
        if (_lastGame is not null)
        {
            var (hwnd, pid, proc) = _lastGame.Value;
            WriteSignal(new OverlaySignal
            {
                Active = true,
                Process = proc,
                Pid = pid,
                Hwnd = hwnd.ToInt64(),
                Utc = DateTimeOffset.UtcNow.ToString("o"),
            });
        }
    }

    /// <summary>游戏退出前台 / 服务停止：写 inactive 信号，前端收到后关闭覆盖层。</summary>
    public void NotifyExit()
    {
        WriteSignal(new OverlaySignal
        {
            Active = false,
            Process = "",
            Pid = 0,
            Hwnd = 0,
            Utc = DateTimeOffset.UtcNow.ToString("o"),
        });
    }

    private void WriteSignal(OverlaySignal signal)
    {
        bool active = signal.Active;
        if (active == _lastActive && active)
        {
            // 持续游戏中：仍需刷新时间戳（前端用它判断后端是否还活着），
            // 但降低频率 —— hwnd 未变时每 3 个检测周期刷一次即可。
            if (Environment.TickCount64 - _lastWriteTick < 6000) return;
        }

        try
        {
            var json = JsonSerializer.Serialize(signal, JsonContext.Default.OverlaySignal);
            var tmp = _signalPath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _signalPath, overwrite: true);
            _lastActive = active;
            _lastWriteTick = Environment.TickCount64;
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"游戏监控：写覆盖层信号失败 {ex.Message}");
        }
    }

    private long _lastWriteTick;

    /// <summary>主程序未运行时拉起它（不等待、不传参 —— 前端自行轮询信号文件）。</summary>
    private void EnsureFrontendRunning()
    {
        try
        {
            var running = Process.GetProcessesByName(FrontendProcessName);
            if (running.Length > 0)
            {
                foreach (var p in running) p.Dispose();
                return;
            }

            if (!File.Exists(_frontendExePath))
            {
                BackEndLog.Warn($"游戏监控：未找到主程序 {_frontendExePath}，无法自动显示覆盖层");
                return;
            }

            BackEndLog.Info("游戏监控：检测到游戏且主程序未运行，自动启动主程序（最小化）");
            Process.Start(new ProcessStartInfo
            {
                FileName = _frontendExePath,
                Arguments = "--game-overlay-auto",
                UseShellExecute = true,
                Verb = "open",
            });
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"游戏监控：启动主程序失败 {ex.Message}");
        }
    }
}

/// <summary>后端 → 前端覆盖层信号（game_overlay_signal.json 内容）。</summary>
public sealed class OverlaySignal
{
    /// <summary>true=前台确认是游戏（显示覆盖层）；false=已退出前台（关闭覆盖层）。</summary>
    [JsonPropertyName("active")]
    public bool Active { get; set; }

    [JsonPropertyName("process")]
    public string Process { get; set; } = "";

    [JsonPropertyName("pid")]
    public uint Pid { get; set; }

    /// <summary>游戏前台窗口句柄（HWND 跨进程有效，前端覆盖层直接绑定跟随）。</summary>
    [JsonPropertyName("hwnd")]
    public long Hwnd { get; set; }

    /// <summary>写入时间（UTC ISO8601）—— 前端判断信号是否过期（后端意外退出）。</summary>
    [JsonPropertyName("utc")]
    public string Utc { get; set; } = "";
}

[JsonSerializable(typeof(OverlaySignal))]
internal sealed partial class JsonContext : JsonSerializerContext;
