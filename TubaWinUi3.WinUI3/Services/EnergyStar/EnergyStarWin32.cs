// EcoQoS / Process Power Throttling Win32 interop.
// Ported from EnergyStarX (https://github.com/JasonWei512/EnergyStarX)
// Copyright 2022 Bingxing Wang — MIT licensed; reproduced under MIT terms (see Services/EnergyStar/LICENSE.txt).

using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace TubaWinUi3.Services;

internal static class EnergyStarWin32
{
    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool QueryFullProcessImageName([In] IntPtr hProcess, [In] int dwFlags, [Out] StringBuilder lpExeName, ref int lpdwSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetProcessInformation([In] IntPtr hProcess,
        [In] ProcessInformationClass processInformationClass, IntPtr processInformation, uint processInformationSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetPriorityClass(IntPtr handle, PriorityClass priorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    [SuppressUnmanagedCodeSecurity]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr hObject);

    public delegate bool WindowEnumProc(IntPtr hwnd, IntPtr lparam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool EnumChildWindows(IntPtr hwnd, WindowEnumProc callback, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWinEventHook(int eventMin, int eventMax,
        IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, int idProcess, int idThread, int dwflags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int UnhookWinEvent(IntPtr hWinEventHook);

    [Flags]
    public enum ProcessAccessFlags : uint
    {
        All = 0x001F0FFF,
        Terminate = 0x00000001,
        CreateThread = 0x00000002,
        VirtualMemoryOperation = 0x00000008,
        VirtualMemoryRead = 0x00000010,
        VirtualMemoryWrite = 0x00000020,
        DuplicateHandle = 0x00000040,
        CreateProcess = 0x000000080,
        SetQuota = 0x00000100,
        SetInformation = 0x00000200,
        QueryInformation = 0x00000400,
        QueryLimitedInformation = 0x00001000,
        Synchronize = 0x00100000
    }

    public enum ProcessInformationClass
    {
        ProcessMemoryPriority,
        ProcessMemoryExhaustionInfo,
        ProcessAppMemoryInfo,
        ProcessInPrivateInfo,
        ProcessPowerThrottling,
        ProcessReservedValue1,
        ProcessTelemetryCoverageInfo,
        ProcessProtectionLevelInfo,
        ProcessLeapSecondInfo,
        ProcessInformationClassMax,
    }

    [Flags]
    public enum ProcessorPowerThrottlingFlags : uint
    {
        None = 0x0,
        ExecutionSpeed = 0x1,
    }

    public enum PriorityClass : uint
    {
        ABOVE_NORMAL_PRIORITY_CLASS = 0x8000,
        BELOW_NORMAL_PRIORITY_CLASS = 0x4000,
        HIGH_PRIORITY_CLASS = 0x80,
        IDLE_PRIORITY_CLASS = 0x40,
        NORMAL_PRIORITY_CLASS = 0x20,
        PROCESS_MODE_BACKGROUND_BEGIN = 0x100000,
        PROCESS_MODE_BACKGROUND_END = 0x200000,
        REALTIME_PRIORITY_CLASS = 0x100
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ProcessPowerThrottlingState
    {
        public const uint CurrentVersion = 1;

        public uint Version;
        public ProcessorPowerThrottlingFlags ControlMask;
        public ProcessorPowerThrottlingFlags StateMask;
    }

    // ---- Window event hook (foreground window change) ----

    private const int EventSystemForeground = 3;
    private const int WinEventOutOfContext = 0;

    private static IntPtr _windowEventHook;
    private static readonly WinEventProc _hookProcDelegate = WindowEventCallback;

    internal delegate void WinEventProc(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    public static void SubscribeToWindowEvents(Action<IntPtr> onForegroundChanged)
    {
        _onForegroundChanged = onForegroundChanged;
        if (_windowEventHook == IntPtr.Zero)
        {
            _windowEventHook = SetWinEventHook(
                EventSystemForeground, EventSystemForeground,
                IntPtr.Zero, _hookProcDelegate, 0, 0, WinEventOutOfContext);
            if (_windowEventHook == IntPtr.Zero)
            {
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
        }
    }

    public static void UnsubscribeWindowEvents()
    {
        if (_windowEventHook != IntPtr.Zero)
        {
            UnhookWinEvent(_windowEventHook);
            _windowEventHook = IntPtr.Zero;
        }
        _onForegroundChanged = null;
    }

    private static Action<IntPtr>? _onForegroundChanged;

    private static void WindowEventCallback(IntPtr hWinEventHook, uint eventType,
        IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        _onForegroundChanged?.Invoke(hwnd);
    }
}
