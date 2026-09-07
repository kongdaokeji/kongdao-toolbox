// DxTraceDiag — 图吧工具箱 FPS 诊断工具
// 用与主应用 FpsService 完全相同的 TraceEvent API，实时收取 DxgKrnl + Win32k 的
// present 相关事件 12 秒，打印出每一路「帧事件」到底有没有发、发给哪个进程、
// 每秒多少。运行方式（管理员终端）：
//   dotnet run --project DxTraceDiag
// 跑之前：把游戏开到前台（全屏/无边框都行），等 12 秒输出完即可。

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;
using Microsoft.Win32;

namespace DxTraceDiag;

public static class Program
{
    private const string DxgKrnlProvider = "802EC45A-1E99-4B83-9920-87C98277BA9D";
    private const string Win32kProvider = "8C416C79-D49B-4F01-A467-E56D3AA8234C";
    private const int CollectSeconds = 12;

    // present 事件家族（PresentMon 同款 ID）
    private static readonly HashSet<ushort> PresentIds = new()
    {
        0x00B8, 0x00AB, 0x00AC, 0x00D7, 0x00A6, 0x0074, 0x0103, 0x0182, 0x00A8, 0x00FC, 0x010A
    };

    // Win32k 窗口化合成事件（PresentMon 用它们跟踪无边框窗口）
    private static readonly HashSet<ushort> Win32kIds = new() { 0x00C9, 0x012D };

    public static int Main()
    {
        if (!IsAdmin())
        {
            Console.WriteLine("[!] 需要管理员权限（ETW 内核会话）。请在管理员终端里运行。");
            return 1;
        }

        Console.WriteLine("==============================================");
        Console.WriteLine(" DxTraceDiag — FPS 帧事件诊断");
        Console.WriteLine($" 收集时长: {CollectSeconds} 秒（请把游戏切到前台）");
        Console.WriteLine("==============================================");
        PrintSystemState();
        Console.WriteLine();

        // 前台窗口信息
        var (fgPid, fgProc, fgTitle) = GetForegroundInfo();
        Console.WriteLine($"[前台窗口] pid={fgPid} 进程={fgProc} 标题=\"{fgTitle}\"");
        Console.WriteLine();

        var counts = new ConcurrentDictionary<int, ProcStats>();   // pid → 统计
        var seconds = new long[CollectSeconds];                     // 每秒总事件数
        long totalEvents = 0, totalPresentLike = 0, totalWin32k = 0;
        var firstTick = 0L;

        const string SessionName = "TubaDxDiag";

        try
        {
            // 先停掉可能残留的同名旧会话
            try { using var old = TraceEventSession.GetActiveSession(SessionName); old?.Stop(); } catch { }

            using var session = new TraceEventSession(SessionName);
            session.EnableProvider(new Guid(DxgKrnlProvider), TraceEventLevel.Verbose, ulong.MaxValue);
            session.EnableProvider(new Guid(Win32kProvider), TraceEventLevel.Verbose, ulong.MaxValue);
            Console.WriteLine("[会话] DxgKrnl + Win32k 已启用，开始收集…\n");

            session.Source.Dynamic.All += ev =>
            {
                try
                {
                    long ts = ev.TimeStamp.Ticks;
                    if (firstTick == 0) firstTick = ts;
                    int sec = (int)((ts - firstTick) / TimeSpan.TicksPerSecond);
                    if (sec >= 0 && sec < CollectSeconds) seconds[sec]++;

                    ushort id = (ushort)(int)ev.ID;
                    bool presentLike = PresentIds.Contains(id);
                    bool win32kEvent = Win32kIds.Contains(id);
                    if (!presentLike && !win32kEvent) return; // 只看相关事件

                    totalEvents++;
                    if (presentLike) totalPresentLike++;
                    if (win32kEvent) totalWin32k++;

                    int pid = ev.ProcessID;
                    var stats = counts.GetOrAdd(pid, _ => new ProcStats(pid));
                    stats.Add(id, ts, presentLike);
                }
                catch { }
            };

            session.Source.Process();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[!] 会话异常: {ex}");
            return 1;
        }

        Console.WriteLine("\n=============== 统计结果 ===============");
        Console.WriteLine($"总相关事件(12秒): {totalEvents}  |  DxgKrnl present 族: {totalPresentLike}  |  Win32k 合成: {totalWin32k}");
        Console.WriteLine($"每秒事件数: {string.Join(" ", seconds)}");

        Console.WriteLine($"\n=== 按进程统计 (top 12，含完整 ID 分布) ===");
        foreach (var s in counts.Values.OrderByDescending(s => s.Total).Take(12))
        {
            var name = GetProcName(s.Pid);
            Console.WriteLine($"\n▶ {name} (pid={s.Pid})  事件总数={s.Total}, present族={s.PresentLike}, 每秒≈{s.Total / (double)CollectSeconds:F1}");
            foreach (var kv in s.ById.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"    ID 0x{kv.Key:X4} × {kv.Value}");
            if (s.Deltas.Count > 0 && s.PresentLike > 5)
            {
                var d = s.Deltas.OrderBy(x => x).ToArray();
                Console.WriteLine($"    帧间隔(中位 {d[d.Length / 2] * 1e-4:F2} ms, min {d[0] * 1e-4:F2} ms, max {d[^1] * 1e-4:F2} ms)");
            }
        }

        // 专门看前台进程
        Console.WriteLine($"\n=== 前台进程 ({fgProc}, pid={fgPid}) present 事件时间戳（前 30 个，间隔毫秒）===");
        if (counts.TryGetValue(fgPid, out var fg))
        {
            long prev = 0;
            int shown = 0;
            foreach (var t in fg.Ticks.Take(30))
            {
                var dt = prev == 0 ? 0 : (t - prev) * 1e-4;
                Console.WriteLine($"  t={t}  Δ={dt:F2} ms");
                prev = t;
                shown++;
            }
            if (shown == 0) Console.WriteLine("  （无任何 present 事件 —— 游戏帧事件没有到达本会话！）");
        }
        else
        {
            Console.WriteLine("  （前台进程完全没有任何事件）");
        }

        PrintConclusion(totalPresentLike, totalWin32k, fgPid, counts);
        return 0;
    }

    private sealed class ProcStats(int pid)
    {
        public int Pid = pid;
        public int Total;
        public int PresentLike;
        public Dictionary<ushort, int> ById = new();
        public List<long> Ticks = new();          // present 族事件时间戳
        public List<long> Deltas = new();         // 帧间隔（present 族）

        public void Add(ushort id, long ts, bool presentLike)
        {
            Total++;
            if (presentLike)
            {
                PresentLike++;
                if (Ticks.Count > 0) Deltas.Add(ts - Ticks[^1]);
                if (Ticks.Count < 2048) Ticks.Add(ts);
            }
            ById.TryGetValue(id, out int c);
            ById[id] = c + 1;
        }
    }

    private static bool IsAdmin()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string GetProcName(int pid)
    {
        try { return Process.GetProcessById(pid).ProcessName; }
        catch { return "?"; }
    }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hWnd, StringBuilder sb, int max);

    private static (int pid, string proc, string title) GetForegroundInfo()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return (0, "?", "");
        GetWindowThreadProcessId(hwnd, out var pid);
        var sb = new StringBuilder(256);
        GetWindowTextW(hwnd, sb, sb.Capacity);
        return ((int)pid, GetProcName((int)pid), sb.ToString());
    }

    private static void PrintSystemState()
    {
        Console.WriteLine("[系统状态]");
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo("logman", "query -ets")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
                }
            };
            p.Start();
            var outText = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            foreach (var l in outText.Split('\n')
                         .Select(l => l.Trim())
                         .Where(l => l.Contains("Tuba") || l.Contains("fps", StringComparison.OrdinalIgnoreCase) || l.Contains("present", StringComparison.OrdinalIgnoreCase)))
                Console.WriteLine($"    {l}");
        }
        catch { }

        // 多实例检查
        try
        {
            var tuba = Process.GetProcesses().Count(pr => pr.ProcessName.StartsWith("TubaWinUi3", StringComparison.OrdinalIgnoreCase));
            Console.WriteLine($"  运行中的 TubaWinUi3 进程数: {tuba}");
        }
        catch { }

        // MPO / 显示相关注册表
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\Dwm");
            var ov = k?.GetValue("OverlayTestMode");
            Console.WriteLine($"  DWM OverlayTestMode (MPO): {(ov == null ? "未设置(默认=MPO 启用)" : ov)}");
        }
        catch { }
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\GraphicsDrivers");
            var hw = k?.GetValue("HwSchMode");
            Console.WriteLine($"  HwSchMode (GPU 硬调度): {(hw == null ? "未设置" : $"{hw} ({((int?)hw == 2 ? "硬件加速 GPU 调度开启" : "关闭")})")}");
        }
        catch { }
    }

    private static void PrintConclusion(long totalPresentLike, long totalWin32k, int fgPid, ConcurrentDictionary<int, ProcStats> counts)
    {
        Console.WriteLine("\n=============== 结论速查 ===============");
        if (totalPresentLike < 30)
            Console.WriteLine("1) DxgKrnl present 事件几乎没收满（12 秒 < 30 个）→ 该游戏/系统的 present 事件不走在 0xB8/0xAB/0xA6… 这几个 ID 上，或 GPU 驱动/MPO 设置导致事件缺失。");
        else
            Console.WriteLine($"1) DxgKrnl present 事件正常（12 秒 {totalPresentLike} 个，≈{totalPresentLike / (double)CollectSeconds:F1} Hz）→ 事件在到达，问题回到应用内部处理。看上方按进程分布：前台游戏进程有没有？");
        if (totalWin32k > 100)
            Console.WriteLine($"2) Win32k 合成事件丰富（{totalWin32k} 个）→ 该游戏是窗口化合成路径，FpsService 需要补 Win32k 事件才能跟踪。");
        if (!counts.ContainsKey(fgPid) && fgPid != 0)
            Console.WriteLine($"3) 前台进程 (pid={fgPid}) 没有任何 present 事件 → 游戏帧事件没按游戏进程到达（走 DWM/其它路径），覆盖层显示 '--'，不可能是 1/2 FPS。若显示 1/2，说明用户看到的是别的进程的 FPS —— 需要检查目标选择。");
        Console.WriteLine("==============================================");
    }
}