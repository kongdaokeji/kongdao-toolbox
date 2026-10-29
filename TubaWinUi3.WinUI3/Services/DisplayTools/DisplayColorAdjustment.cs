using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services.DisplayTools;

internal sealed record DisplayColorSettings(double Temperature = 6500, double Brightness = 100,
    double Contrast = 100, double Gamma = 1, double Red = 100, double Green = 100, double Blue = 100)
{
    public static DisplayColorSettings Preset(string id) => id switch
    {
        "warm" => new(4500), "cool" => new(8000), "soft" => new(6000, 95, 90),
        "bright" => new(6500, 105, 100, 1.08), "contrast" => new(6500, 100, 112), _ => new()
    };

    public ushort[] BuildRamp(ushort[] baseline)
    {
        if (baseline.Length != 768) throw new ArgumentException("颜色曲线必须包含 3 × 256 个采样。", nameof(baseline));
        if (!double.IsFinite(Temperature) || Temperature is < 3500 or > 9000 ||
            !double.IsFinite(Brightness) || Brightness is < 75 or > 110 ||
            !double.IsFinite(Contrast) || Contrast is < 80 or > 120 ||
            !double.IsFinite(Gamma) || Gamma is < .8 or > 1.3 ||
            new[] { Red, Green, Blue }.Any(v => !double.IsFinite(v) || v is < 75 or > 110))
            throw new ArgumentOutOfRangeException(nameof(Temperature), "颜色参数超出支持范围。");

        // Relative channel scaling keeps 6500 K exactly neutral and preserves the user's original LUT.
        double[] Neutral(double kelvin)
        {
            double t = kelvin / 100;
            double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -.1332047592);
            double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -.0755148492);
            double b = t >= 66 ? 255 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
            return [Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255)];
        }
        var white = Neutral(Temperature); var neutral = Neutral(6500);
        double[] gains = [Red / 100 * white[0] / neutral[0], Green / 100 * white[1] / neutral[1], Blue / 100 * white[2] / neutral[2]];
        var ramp = new ushort[768];
        for (int channel = 0; channel < 3; channel++)
            for (int i = 0; i < 256; i++)
            {
                double x = Math.Clamp((i / 255.0 - .5) * Contrast / 100 + .5, 0, 1);
                x = Math.Clamp(Math.Pow(x, 1 / Gamma) * Brightness / 100 * gains[channel], 0, 1) * 255;
                int lo = (int)x, hi = Math.Min(lo + 1, 255);
                ramp[channel * 256 + i] = (ushort)Math.Round(baseline[channel * 256 + lo] * (1 - (x - lo)) + baseline[channel * 256 + hi] * (x - lo));
            }
        return ramp;
    }
}

/// <summary>Transactional gamma changes. Failed/ignored writes and unconfirmed previews are rolled back.</summary>
internal sealed class DisplayGammaSession
{
    private readonly object _gate = new();
    private readonly Func<ushort[]> _read;
    private readonly Action<ushort[]> _write;
    private readonly ushort[] _original;
    private ushort[] _confirmed;
    private ushort[]? _applied;
    private System.Threading.Timer? _rollback;
    private long _generation;
    public DisplayColorSettings Settings { get; private set; } = new();
    private DisplayColorSettings _confirmedSettings = new();
    private DateTimeOffset? _previewDeadline;
    public DateTimeOffset? PreviewDeadline
    {
        get { lock (_gate) return _previewDeadline; }
        private set => _previewDeadline = value;
    }
    public string? RestoreError { get; private set; }

    public DisplayGammaSession(Func<ushort[]> read, Action<ushort[]> write)
    {
        _read = read; _write = write; _original = read();
        if (_original.Length != 768) throw new InvalidOperationException("驱动返回的颜色曲线不完整。");
        _confirmed = (ushort[])_original.Clone();
    }

    public void Preview(DisplayColorSettings settings, TimeSpan? duration = null)
    {
        lock (_gate)
        {
            CancelPreviewCore();
            var ramp = settings.BuildRamp(_original);
            try
            {
                _write(ramp);
                if (!Matches(_read(), ramp)) throw new InvalidOperationException("驱动未接受颜色调节，已恢复原始效果。HDR、夜间模式或其他校色软件可能接管了颜色曲线。");
                _applied = ramp; Settings = settings; RestoreError = null;
                var timeout = duration ?? TimeSpan.FromSeconds(20);
                PreviewDeadline = DateTimeOffset.UtcNow + timeout;
                long generation = _generation;
                _rollback = new System.Threading.Timer(_ => { lock (_gate) { if (_generation == generation) CancelPreview(); } }, null, timeout, Timeout.InfiniteTimeSpan);
            }
            catch
            {
                _write(_confirmed); _applied = null; Settings = _confirmedSettings;
                throw;
            }
        }
    }

    internal static bool Matches(ushort[] actual, ushort[] expected) => actual.Length == 768 && expected.Length == 768 &&
        actual.Zip(expected).All(p => Math.Abs((int)p.First - p.Second) <= 512); // allow 8-bit LUT quantization

    public void Confirm()
    {
        lock (_gate)
        {
            if (PreviewDeadline is not { } deadline || deadline <= DateTimeOffset.UtcNow || _applied is null)
            {
                CancelPreviewCore();
                throw new InvalidOperationException("预览已结束，请重新应用。");
            }
            if (!Matches(_read(), _applied)) { CancelPreviewCore(); throw new InvalidOperationException("颜色曲线已被系统或其他程序覆盖，请重新应用。"); }
            _confirmed = _applied; _confirmedSettings = Settings;
            _generation++;
            _rollback?.Dispose(); _rollback = null; PreviewDeadline = null;
        }
    }

    public void CancelPreview() { lock (_gate) { try { CancelPreviewCore(); } catch (Exception ex) { PreviewDeadline = null; RestoreError = ex.Message; Debug.WriteLine(ex); } } }
    private void CancelPreviewCore()
    {
        _generation++;
        _rollback?.Dispose(); _rollback = null;
        if (PreviewDeadline is not null)
        {
            // Do not overwrite a newer curve installed by Night Light or another calibration app.
            if (_applied is not null && Matches(_read(), _applied)) _write(_confirmed);
            PreviewDeadline = null; Settings = _confirmedSettings; _applied = null;
        }
    }
    public void Restore(bool onlyIfOwned = false)
    {
        lock (_gate)
        {
            _generation++;
            _rollback?.Dispose(); _rollback = null;
            if (onlyIfOwned && !Matches(_read(), _applied ?? _confirmed)) { PreviewDeadline = null; return; }
            _write(_original);
            if (!Matches(_read(), _original)) throw new InvalidOperationException("驱动未确认原始颜色曲线，恢复未完成。");
            _confirmed = (ushort[])_original.Clone(); _applied = null;
            Settings = _confirmedSettings = new(); PreviewDeadline = null; RestoreError = null;
        }
    }
}

internal static class DisplayColorAdjustment
{
    private static readonly Dictionary<string, DisplayGammaSession> Sessions = new(StringComparer.OrdinalIgnoreCase);
    static DisplayColorAdjustment() => AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreAll();
    public static DisplayGammaSession? Find(string key) { lock (Sessions) return Sessions.GetValueOrDefault(key); }
    public static void Reset(string key)
    {
        lock (Sessions)
            if (Sessions.TryGetValue(key, out var session)) { session.Restore(); Sessions.Remove(key); }
    }
    public static DisplayGammaSession Get(DisplayInspection display)
    {
        if (display.AdvancedColorActive != false) throw new InvalidOperationException("请在 SDR 模式下使用滤镜；当前为 HDR / 高级颜色模式，或系统未能确认颜色状态。");
        lock (Sessions)
        {
            if (!Sessions.TryGetValue(display.InterfaceId, out var session))
            {
                if (string.IsNullOrEmpty(display.InterfaceId)) throw new InvalidOperationException("无法确认所选显示器的设备身份。");
                session = new DisplayGammaSession(() => Read(ResolveDevice(display.InterfaceId)), ramp => Write(ResolveDevice(display.InterfaceId), ramp));
                Sessions.Add(display.InterfaceId, session);
            }
            return session;
        }
    }
    public static void RestoreAll()
    {
        lock (Sessions)
            foreach (var session in Sessions.Values)
                try { session.Restore(onlyIfOwned: true); } catch (Exception ex) { Debug.WriteLine($"[DisplayColor] Restore: {ex}"); }
    }
    private static ushort[] Read(string device)
    {
        nint dc = CreateDCW("DISPLAY", device, null, 0);
        if (dc == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "无法打开所选显示器。");
        try { var ramp = new ushort[768]; if (!GetDeviceGammaRamp(dc, ramp)) throw new InvalidOperationException("显示驱动不支持读取颜色曲线。"); return ramp; }
        finally { DeleteDC(dc); }
    }
    private static string ResolveDevice(string interfaceId)
    {
        for (uint i = 0; ; i++)
        {
            var adapter = new Device { Size = Marshal.SizeOf<Device>() };
            if (!EnumDisplayDevicesW(null, i, ref adapter, 0)) break;
            if ((adapter.Flags & 1) == 0) continue;
            for (uint j = 0; ; j++)
            {
                var monitor = new Device { Size = Marshal.SizeOf<Device>() };
                if (!EnumDisplayDevicesW(adapter.Name, j, ref monitor, 1)) break;
                if ((monitor.Flags & 1) != 0 && string.Equals(monitor.Id, interfaceId, StringComparison.OrdinalIgnoreCase)) return adapter.Name;
            }
        }
        throw new InvalidOperationException("所选显示器已断开，没有修改其他屏幕。");
    }
    private static void Write(string device, ushort[] ramp)
    {
        nint dc = CreateDCW("DISPLAY", device, null, 0);
        if (dc == 0) throw new InvalidOperationException("显示器已断开或无法访问。");
        try { if (!SetDeviceGammaRamp(dc, ramp)) throw new InvalidOperationException("显示驱动拒绝了颜色调节。"); }
        finally { DeleteDC(dc); }
    }
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern nint CreateDCW(string driver, string device, string? output, nint init);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Device
    {
        public int Size;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Key;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumDisplayDevicesW(string? device, uint index, ref Device info, uint flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool GetDeviceGammaRamp(nint dc, [Out] ushort[] ramp);
    [DllImport("gdi32.dll")] private static extern bool SetDeviceGammaRamp(nint dc, [In] ushort[] ramp);
}
