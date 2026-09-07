using System.Diagnostics;
using System.Runtime.InteropServices;
using TubaWinUI3.BackEnd.Models;

namespace TubaWinUI3.BackEnd;

/// <summary>
/// 主动拦截后端入口（架构移植自 ContextMenuMgr 的 BackendRuntime 职责：
/// 存储 → 监视器 → 命名管道服务器，三者共享同一组存储实例并互相接线）。
/// 用法：TubaWinUI3.BackEnd.exe [--config &lt;路径&gt;] [--once] [--stop]
/// - --config：后端配置文件（JSON，含 DataDir / PollIntervalSeconds / LogFile / NotifyMode）。
/// - --once：只执行一轮扫描后退出（诊断/测试用）。
/// - --stop：通知已运行的后端正例退出。
/// 单实例：通过命名互斥锁保证同时只有一个后端进程运行。
/// </summary>
internal static class Program
{
    private const string MutexName = "Global\\TubaWinUi3_ActiveIntercept_Backend";

    private static int Main(string[] args)
    {
        // 常驻托盘服务：隐藏自身控制台窗口（后台服务不弹黑窗口）。
        // 但 --help 时保留控制台方便查看用法。
        if (!args.Any(a => a is "--help" or "-h" or "-?"))
        {
            HideConsoleWindow();
        }

        if (args.Any(a => string.Equals(a, "--help", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "-h", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "-?", StringComparison.OrdinalIgnoreCase)))
        {
            Console.WriteLine("TubaWinUI3.BackEnd — 主动拦截后端（流氓软件拦截器，NativeAOT）");
            Console.WriteLine();
            Console.WriteLine("用法：TubaWinUI3.BackEnd.exe [--config <路径>] [--once] [--stop]");
            Console.WriteLine("  --config  后端配置文件（JSON：DataDir / PollIntervalSeconds / LogFile / NotifyMode）");
            Console.WriteLine("  --once    只执行一轮扫描后退出（诊断用）");
            Console.WriteLine("  --stop    通知已运行的后端退出");
            return 0;
        }

        // --stop：通过互斥锁通知已运行实例退出
        if (args.Any(a => string.Equals(a, "--stop", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (Mutex.TryOpenExisting(MutexName, out var existing))
                {
                    existing.ReleaseMutex();
                    existing.Dispose();
                    Console.WriteLine("已通知后端退出。");
                }
                else
                {
                    Console.WriteLine("后端未在运行。");
                }
            }
            catch { }
            return 0;
        }

        // Toast 通知被点击时，Windows 通过 COM 服务器启动后端并传入 --toast-handler。
        // 此时启动主程序跳转审核页，然后退出。
        if (args.Any(a => string.Equals(a, "--toast-handler", StringComparison.OrdinalIgnoreCase)))
        {
            LaunchMainApp("--show-active-intercept");
            return 0;
        }

        // 单实例互斥锁
        using var mutex = new Mutex(true, MutexName, out bool createdNew);
        if (!createdNew)
        {
            try
            {
                if (!mutex.WaitOne(2000))
                {
                    Console.Error.WriteLine("已有后端实例在运行，退出。");
                    return 0;
                }
            }
            catch (AbandonedMutexException)
            {
                // 之前的实例崩溃了，接管
            }
        }

        string? configPath = null;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], "--config", StringComparison.OrdinalIgnoreCase))
            {
                configPath = args[i + 1];
                break;
            }
        }
        bool once = args.Any(a => string.Equals(a, "--once", StringComparison.OrdinalIgnoreCase));

        var config = BackendConfigLoader.Load(configPath ?? "");
        if (string.IsNullOrWhiteSpace(config.DataDir))
        {
            Console.Error.WriteLine("错误：配置缺少 DataDir（--config 指向的 JSON 必须包含 DataDir）。");
            return 2;
        }

        BackEndLog.Configure(config.LogFile);
        BackEndLog.Info($"主动拦截后端启动（PID {Environment.ProcessId}），配置：{configPath ?? "(默认)"}");

        NotificationHelper.RegisterComServer();

        // ---- 装配（共享同一组存储实例）----
        var monitor = new InterceptMonitor(config);
        var handler = new InterceptRequestHandler(
            Path.Combine(config.DataDir, "active_intercept"),
            monitor.State,
            monitor.Events,
            monitor.BlockEngine,
            monitor.Policies,
            monitor.Ignore,
            monitor.Notifications);
        var server = new NamedPipeBackendServer(handler.DispatchAsync);

        using var cts = new CancellationTokenSource();

        // 推送：监视器与管道处理器都直通管道广播
        monitor.Notify = server.BroadcastNotification;
        handler.Notify = server.BroadcastNotification;

        // 优雅停机：任何一方请求退出 → 取消令牌
        handler.ShutdownRequested += (_, _) => SafeCancel(cts);
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            SafeCancel(cts);
        };
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeCancel(cts);

        server.Start(cts.Token);
        BackEndLog.Info($"命名管道服务器已启动：{InterceptPipeConstants.PipeName}");

        // 系统托盘（常驻模式；--once 诊断模式不建托盘）
        BackendTrayHost? tray = null;
        if (!once)
        {
            tray = new BackendTrayHost("图吧工具箱 · 主动拦截已开启", config.DataDir, () => SafeCancel(cts));
            tray.Start();
        }

        try
        {
            if (once)
            {
                monitor.RunOnce();
                BackEndLog.Info("--once 单轮执行完成");
            }
            else
            {
                monitor.Run();
                BackEndLog.Info("主动拦截后端退出");
            }
            return 0;
        }
        catch (Exception ex)
        {
            BackEndLog.Error($"后端运行失败：{ex}");
            return 1;
        }
        finally
        {
            tray?.Dispose();
            server.BroadcastServiceStopping();
            server.Stop();
        }
    }

    private static void SafeCancel(CancellationTokenSource cts)
    {
        try { cts.Cancel(); } catch { }
    }

    // ---------- 控制台窗口隐藏 ----------

    private const int SW_HIDE = 0;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>隐藏后端自身的控制台窗口（常驻托盘服务无黑窗口）。失败静默忽略。</summary>
    private static void HideConsoleWindow()
    {
        try
        {
            var hwnd = GetConsoleWindow();
            if (hwnd != IntPtr.Zero) ShowWindow(hwnd, SW_HIDE);
        }
        catch
        {
            // 非交互环境（如服务/无控制台）下静默忽略
        }
    }

    private static void LaunchMainApp(string arg)
    {
        try
        {
            var exe = NotificationHelper.FindMainAppExe();
            if (string.IsNullOrEmpty(exe))
            {
                BackEndLog.Error("找不到主程序：TubaWinUi3.exe");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arg,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            BackEndLog.Error($"启动主程序失败：{ex.Message}");
        }
    }
}