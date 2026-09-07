// EcoQoS (Efficiency Mode) background-process throttling engine.
// Ported from EnergyStarX (https://github.com/JasonWei512/EnergyStarX)
// Copyright 2022 Bingxing Wang — MIT licensed (see Services/EnergyStar/LICENSE.txt).
//
// Adapted for TubaWinUi3:
//   - static service (no DI), settings persisted via AppSettings
//   - NLog replaced by an optional Log event
//   - WindowService dependency removed (in-process, app lifecycle handled by caller)
//   - whitelist / blacklist defaults provided by EnergyStarDefaults

using System.Diagnostics;
using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Windows.System.Power;

namespace TubaWinUi3.Services;

/// <summary>
/// Wraps Windows 11's Process Power Throttling (EcoQoS / Efficiency Mode) to
/// throttle background processes for better battery life / thermals while
/// keeping the foreground application responsive.
/// </summary>
public static class EnergyStarService
{
    private static readonly object LockObj = new();
    private static CancellationTokenSource? _houseKeepingCts;
    private static Task? _houseKeepingTask;

    private const string UWPFrameHostApp = "ApplicationFrameHost.exe";

    // AppSettings keys
    private const string KThrottleWhenPluggedIn = "EnergyStarThrottleWhenPluggedIn";
    private const string KWhitelist = "EnergyStarProcessWhitelist";
    private const string KBlacklist = "EnergyStarProcessBlacklist";

    // Pre-allocated throttle / unthrottle control blocks (pinned native memory).
    private static readonly IntPtr PThrottleOn;
    private static readonly IntPtr PThrottleOff;
    private static readonly int SzControlBlock;

    private static uint _pendingProcPid;
    private static string _pendingProcName = "";

    public static ThrottleStatus ThrottleStatus { get; private set; } = ThrottleStatus.Stopped;

    private static bool _pauseThrottling;
    private static bool _initialized;
    private static IReadOnlySet<string> _processWhitelist = new HashSet<string>();
    private static IReadOnlySet<string> _wildcardProcessWhitelist = new HashSet<string>();
    private static IReadOnlySet<string> _processBlacklist = new HashSet<string>();
    private static IReadOnlySet<string> _wildcardProcessBlacklist = new HashSet<string>();

    static EnergyStarService()
    {
        SzControlBlock = Marshal.SizeOf<EnergyStarWin32.ProcessPowerThrottlingState>();
        PThrottleOn = Marshal.AllocHGlobal(SzControlBlock);
        PThrottleOff = Marshal.AllocHGlobal(SzControlBlock);

        var throttleState = new EnergyStarWin32.ProcessPowerThrottlingState
        {
            Version = EnergyStarWin32.ProcessPowerThrottlingState.CurrentVersion,
            ControlMask = EnergyStarWin32.ProcessorPowerThrottlingFlags.ExecutionSpeed,
            StateMask = EnergyStarWin32.ProcessorPowerThrottlingFlags.ExecutionSpeed,
        };
        var unthrottleState = new EnergyStarWin32.ProcessPowerThrottlingState
        {
            Version = EnergyStarWin32.ProcessPowerThrottlingState.CurrentVersion,
            ControlMask = EnergyStarWin32.ProcessorPowerThrottlingFlags.ExecutionSpeed,
            StateMask = EnergyStarWin32.ProcessorPowerThrottlingFlags.None,
        };
        Marshal.StructureToPtr(throttleState, PThrottleOn, false);
        Marshal.StructureToPtr(unthrottleState, PThrottleOff, false);
    }

    public static event EventHandler<ThrottleStatus>? ThrottleStatusChanged;
    public static event EventHandler<string>? Log;

    public static bool PauseThrottling
    {
        get => _pauseThrottling;
        set
        {
            lock (LockObj)
            {
                if (_pauseThrottling != value)
                {
                    _pauseThrottling = value;
                    UpdateThrottleStatus();
                }
            }
        }
    }

    public static bool ThrottleWhenPluggedIn
    {
        get => AppSettings.GetBool(KThrottleWhenPluggedIn, false);
        set
        {
            lock (LockObj)
            {
                if (AppSettings.GetBool(KThrottleWhenPluggedIn, false) != value)
                {
                    AppSettings.Set(KThrottleWhenPluggedIn, value);
                    UpdateThrottleStatus();
                }
            }
        }
    }

    public static bool IsOnBattery => PowerManager.PowerSourceKind == PowerSourceKind.DC;

    public static IReadOnlySet<string> ProcessWhitelist => _processWhitelist;
    public static IReadOnlySet<string> ProcessBlacklist => _processBlacklist;

    public static string ProcessWhitelistString => AppSettings.Get(KWhitelist) ?? EnergyStarDefaults.DefaultProcessWhitelist;
    public static string ProcessBlacklistString => AppSettings.Get(KBlacklist) ?? EnergyStarDefaults.DefaultProcessBlacklist;

    public static void Initialize()
    {
        lock (LockObj)
        {
            if (_initialized) return;

            EnergyStarWin32.SubscribeToWindowEvents(OnForegroundWindowChanged);

            ApplyProcessWhitelist(ProcessWhitelistString);
            ApplyProcessBlacklist(ProcessBlacklistString);

            UpdateThrottleStatus();
            PowerManager.PowerSourceKindChanged += OnPowerSourceKindChanged;

            _initialized = true;
            Log?.Invoke(null, $"EnergyStar 已启动 (状态: {ThrottleStatus})");
        }
    }

    public static void Shutdown()
    {
        lock (LockObj)
        {
            if (!_initialized) return;

            PowerManager.PowerSourceKindChanged -= OnPowerSourceKindChanged;
            StopThrottling(ThrottleStatus);

            EnergyStarWin32.UnsubscribeWindowEvents();
            _initialized = false;
            Log?.Invoke(null, "EnergyStar 已停止");
        }
    }

    // ---- whitelist / blacklist API ----

    public static void ApplyAndSaveProcessWhitelist(string processWhitelistString)
    {
        lock (LockObj)
        {
            ApplyProcessWhitelist(processWhitelistString);
            AppSettings.Set(KWhitelist, processWhitelistString);
        }
    }

    public static void ApplyProcessWhitelist(string processWhitelistString)
    {
        lock (LockObj)
        {
            var prev = ThrottleStatus;
            if (prev != ThrottleStatus.Stopped) StopThrottling(prev);

            (var full, var wildcard) = ParseProcessList(processWhitelistString);
            _processWhitelist = full;
            _wildcardProcessWhitelist = wildcard;

            Log?.Invoke(null, $"已应用进程白名单 ({full.Count} 项)");

            if (prev != ThrottleStatus.Stopped) StartThrottling(prev);
        }
    }

    public static void ApplyAndSaveProcessBlacklist(string processBlacklistString)
    {
        lock (LockObj)
        {
            ApplyProcessBlacklist(processBlacklistString);
            AppSettings.Set(KBlacklist, processBlacklistString);
        }
    }

    public static void ApplyProcessBlacklist(string processBlacklistString)
    {
        lock (LockObj)
        {
            var prev = ThrottleStatus;
            if (prev != ThrottleStatus.Stopped) StopThrottling(prev);

            (var full, var wildcard) = ParseProcessList(processBlacklistString);
            _processBlacklist = full;
            _wildcardProcessBlacklist = wildcard;

            Log?.Invoke(null, $"已应用进程黑名单 ({full.Count} 项)");

            if (prev != ThrottleStatus.Stopped) StartThrottling(prev);
        }
    }

    public static void RestoreDefaultProcessWhitelist() => ApplyAndSaveProcessWhitelist(EnergyStarDefaults.DefaultProcessWhitelist);
    public static void RestoreDefaultProcessBlacklist() => ApplyAndSaveProcessBlacklist(EnergyStarDefaults.DefaultProcessBlacklist);

    // ---- core throttle logic ----

    private static (HashSet<string> fullProcessList, HashSet<string> wildcardProcessList) ParseProcessList(string processListString)
    {
        var full = new HashSet<string>();
        var wildcard = new HashSet<string>();
        var doubleSlashRegex = new Regex("//");

        using var reader = new StringReader(processListString);
        while (reader.ReadLine() is string line)
        {
            var match = doubleSlashRegex.Match(line);
            var name = (match.Success ? line[..match.Index] : line).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(name)) continue;

            full.Add(name);
            if (name.Contains('?') || name.Contains('*'))
                wildcard.Add(name);
        }

        return (full, wildcard);
    }

    private static void UpdateThrottleStatus()
    {
        lock (LockObj)
        {
            var fromThrottleStatus = ThrottleStatus;
            var toThrottleStatus = (PauseThrottling, IsOnBattery, ThrottleWhenPluggedIn) switch
            {
                (true, _, _) => ThrottleStatus.Stopped,
                (false, true, _) => ThrottleStatus.BlacklistAndAllButWhitelist,
                (false, false, true) => ThrottleStatus.BlacklistAndAllButWhitelist,
                (false, false, false) => ThrottleStatus.OnlyBlacklist
            };

            bool changed = (fromThrottleStatus, toThrottleStatus) switch
            {
                (ThrottleStatus.Stopped, ThrottleStatus.OnlyBlacklist) => StartThrottling(toThrottleStatus),
                (ThrottleStatus.Stopped, ThrottleStatus.BlacklistAndAllButWhitelist) => StartThrottling(toThrottleStatus),

                (ThrottleStatus.OnlyBlacklist, ThrottleStatus.Stopped) => StopThrottling(fromThrottleStatus),
                (ThrottleStatus.OnlyBlacklist, ThrottleStatus.BlacklistAndAllButWhitelist) => ThrottleUserBackgroundProcesses(toThrottleStatus),

                (ThrottleStatus.BlacklistAndAllButWhitelist, ThrottleStatus.Stopped) => StopThrottling(fromThrottleStatus),
                (ThrottleStatus.BlacklistAndAllButWhitelist, ThrottleStatus.OnlyBlacklist) => RecoverUserProcesses(fromThrottleStatus) && ThrottleUserBackgroundProcesses(toThrottleStatus),

                _ when fromThrottleStatus == toThrottleStatus => false,
                _ => throw new ArgumentException($"Unknown ThrottleStatus transition: {fromThrottleStatus} -> {toThrottleStatus}")
            };

            if (changed)
            {
                ThrottleStatus = toThrottleStatus;
                ThrottleStatusChanged?.Invoke(null, ThrottleStatus);
                Log?.Invoke(null, $"节流状态变更为: {toThrottleStatus}");
            }
        }
    }

    private static bool StartThrottling(ThrottleStatus toThrottleStatus)
    {
        lock (LockObj)
        {
            if (toThrottleStatus == ThrottleStatus.Stopped) return false;

            Log?.Invoke(null, "开始节流后台进程");
            ThrottleUserBackgroundProcesses(toThrottleStatus);
            _houseKeepingCts = new CancellationTokenSource();
            _houseKeepingTask = HouseKeeping(_houseKeepingCts.Token);
            return true;
        }
    }

    private static bool StopThrottling(ThrottleStatus fromThrottleStatus)
    {
        lock (LockObj)
        {
            if (fromThrottleStatus == ThrottleStatus.Stopped) return false;

            Log?.Invoke(null, "停止节流后台进程");
            _houseKeepingCts?.Cancel();
            RecoverUserProcesses(fromThrottleStatus);
            return true;
        }
    }

    private static bool ThrottleUserBackgroundProcesses(ThrottleStatus toThrottleStatus)
    {
        lock (LockObj)
        {
            if (toThrottleStatus == ThrottleStatus.Stopped) return false;

            var running = Process.GetProcesses();
            int currentSession = Process.GetCurrentProcess().SessionId;

            foreach (var proc in running.Where(p => p.SessionId == currentSession))
            {
                if (proc.Id == _pendingProcPid) continue;
                if (ShouldBypassProcess($"{proc.ProcessName}.exe".ToLowerInvariant(), toThrottleStatus)) continue;
                var h = EnergyStarWin32.OpenProcess((uint)EnergyStarWin32.ProcessAccessFlags.SetInformation, false, (uint)proc.Id);
                ToggleEfficiencyMode(h, true);
                EnergyStarWin32.CloseHandle(h);
            }
            return true;
        }
    }

    private static bool RecoverUserProcesses(ThrottleStatus fromThrottleStatus)
    {
        lock (LockObj)
        {
            if (fromThrottleStatus == ThrottleStatus.Stopped) return false;

            var running = Process.GetProcesses();
            int currentSession = Process.GetCurrentProcess().SessionId;

            foreach (var proc in running.Where(p => p.SessionId == currentSession))
            {
                if (ShouldBypassProcess($"{proc.ProcessName}.exe".ToLowerInvariant(), fromThrottleStatus)) continue;
                var h = EnergyStarWin32.OpenProcess((uint)EnergyStarWin32.ProcessAccessFlags.SetInformation, false, (uint)proc.Id);
                ToggleEfficiencyMode(h, false);
                EnergyStarWin32.CloseHandle(h);
            }
            return true;
        }
    }

    private static async Task HouseKeeping(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), ct);
                ThrottleUserBackgroundProcesses(ThrottleStatus);
                Log?.Invoke(null, "定期清理任务：节流后台进程");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) { Log?.Invoke(null, $"定期清理任务出错: {e.Message}"); }
        }
    }

    private static void OnForegroundWindowChanged(IntPtr hwnd)
    {
        lock (LockObj)
        {
            if (ThrottleStatus == ThrottleStatus.Stopped) return;

            uint windowThreadId = EnergyStarWin32.GetWindowThreadProcessId(hwnd, out uint procId);
            if (windowThreadId == 0 || procId == 0) return;

            IntPtr procHandle = EnergyStarWin32.OpenProcess(
                (uint)(EnergyStarWin32.ProcessAccessFlags.QueryLimitedInformation | EnergyStarWin32.ProcessAccessFlags.SetInformation),
                false, procId);
            if (procHandle == IntPtr.Zero) return;

            string appName = GetProcessNameFromHandle(procHandle);

            if (appName == UWPFrameHostApp)
            {
                bool found = false;
                EnergyStarWin32.EnumChildWindows(hwnd, (innerHwnd, _) =>
                {
                    if (found) return true;
                    if (EnergyStarWin32.GetWindowThreadProcessId(innerHwnd, out uint innerProcId) > 0)
                    {
                        if (procId == innerProcId) return true;

                        var inner = EnergyStarWin32.OpenProcess(
                            (uint)(EnergyStarWin32.ProcessAccessFlags.QueryLimitedInformation | EnergyStarWin32.ProcessAccessFlags.SetInformation),
                            false, innerProcId);
                        if (inner == IntPtr.Zero) return true;

                        found = true;
                        EnergyStarWin32.CloseHandle(procHandle);
                        procHandle = inner;
                        procId = innerProcId;
                        appName = GetProcessNameFromHandle(procHandle);
                    }
                    return true;
                }, IntPtr.Zero);
            }

            bool bypass = ShouldBypassProcess(appName, ThrottleStatus);
            if (!bypass)
            {
                Log?.Invoke(null, $"提升前台应用: {appName}");
                ToggleEfficiencyMode(procHandle, false);
            }

            if (_pendingProcPid != 0)
            {
                Log?.Invoke(null, $"重新节流上一个前台应用: {_pendingProcName}");
                var prev = EnergyStarWin32.OpenProcess((uint)EnergyStarWin32.ProcessAccessFlags.SetInformation, false, _pendingProcPid);
                if (prev != IntPtr.Zero)
                {
                    ToggleEfficiencyMode(prev, true);
                    EnergyStarWin32.CloseHandle(prev);
                    _pendingProcPid = 0;
                    _pendingProcName = "";
                }
            }

            if (!bypass)
            {
                _pendingProcPid = procId;
                _pendingProcName = appName;
            }

            EnergyStarWin32.CloseHandle(procHandle);
        }
    }

    private static bool ShouldBypassProcess(string processName, ThrottleStatus throttleStatus) =>
        !ShouldThrottleProcess(processName, throttleStatus);

    private static bool ShouldThrottleProcess(string processName, ThrottleStatus throttleStatus) => throttleStatus switch
    {
        ThrottleStatus.Stopped => false,
        ThrottleStatus.OnlyBlacklist => IsProcessInBlacklist(processName),
        ThrottleStatus.BlacklistAndAllButWhitelist => IsProcessInBlacklist(processName) || !IsProcessInWhitelist(processName),
        _ => throw new ArgumentException("Unknown ThrottleStatus")
    };

    private static bool IsProcessInWhitelist(string processName) =>
        IsProcessInList(processName, _processWhitelist, _wildcardProcessWhitelist);

    private static bool IsProcessInBlacklist(string processName) =>
        IsProcessInList(processName, _processBlacklist, _wildcardProcessBlacklist);

    private static bool IsProcessInList(string processName, IReadOnlySet<string> fullList, IReadOnlySet<string> wildcardList)
    {
        var name = processName.ToLowerInvariant();
        if (fullList.Contains(name)) return true;

        foreach (var wildcard in wildcardList)
        {
            if (FileSystemName.MatchesSimpleExpression(wildcard, name, ignoreCase: true))
                return true;
        }
        return false;
    }

    private static void ToggleEfficiencyMode(IntPtr hProcess, bool enable)
    {
        EnergyStarWin32.SetProcessInformation(hProcess,
            EnergyStarWin32.ProcessInformationClass.ProcessPowerThrottling,
            enable ? PThrottleOn : PThrottleOff,
            (uint)SzControlBlock);
        EnergyStarWin32.SetPriorityClass(hProcess,
            enable ? EnergyStarWin32.PriorityClass.IDLE_PRIORITY_CLASS : EnergyStarWin32.PriorityClass.NORMAL_PRIORITY_CLASS);
    }

    private static void OnPowerSourceKindChanged(object? sender, object e)
    {
        lock (LockObj)
        {
            Log?.Invoke(null, IsOnBattery ? "电源切换到电池" : "电源切换到交流电");
            UpdateThrottleStatus();
        }
    }

    private static string GetProcessNameFromHandle(IntPtr hProcess)
    {
        int capacity = 1024;
        var sb = new StringBuilder(capacity);
        if (EnergyStarWin32.QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
        {
            return Path.GetFileName(sb.ToString());
        }
        return "";
    }

    public static string ThrottleStatusDescription(ThrottleStatus s) => s switch
    {
        ThrottleStatus.Stopped => "已停止 / 暂停",
        ThrottleStatus.OnlyBlacklist => "插电模式：仅节流黑名单",
        ThrottleStatus.BlacklistAndAllButWhitelist => "节流模式：全员 (除白名单)",
        _ => s.ToString()
    };
}
