using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TubaWinUi3.Services.DisplayTools;

internal sealed record DisplayProfileOption(string Name, string Path, bool IsNative = false)
{
    public override string ToString() => Name;
}

/// <summary>Windows per-user SDR profile associations; no file import or control-panel handoff.</summary>
internal static class DisplayColorProfiles
{
    private sealed record Backup(bool PerUser, string? UserDefault);
    private static readonly Dictionary<string, Backup> Backups = new(StringComparer.OrdinalIgnoreCase);
    private const uint CurrentUser = 1, Icc = 0, None = 4, MonitorClass = 0x6D6E7472;
    public static bool CanRestore(DisplayInspection display) { lock (Backups) return Backups.ContainsKey(display.ColorDeviceKey); }
    private static string DirectoryPath
    {
        get { var b = new StringBuilder(32768); uint bytes = 65536; if (!GetColorDirectoryW(null, b, ref bytes)) throw Error("读取颜色配置目录失败"); return b.ToString(); }
    }
    public static List<DisplayProfileOption> List(DisplayInspection display)
    {
        var result = new List<DisplayProfileOption>();
        if (DisplayGamut.IsValid(display.Primaries) && display.WhitePoint is not null)
            result.Add(new("显示器原生 · 根据 EDID 自动生成", "", true));
        foreach (var path in Directory.EnumerateFiles(DirectoryPath).OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
        {
            if (System.IO.Path.GetFileName(path).StartsWith("TubaDisplay_", StringComparison.OrdinalIgnoreCase)) continue;
            if (!new[] { ".icc", ".icm" }.Contains(System.IO.Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)) continue;
            try
            {
                using var file = File.OpenRead(path); var header = new byte[128]; file.ReadExactly(header);
                if (!IsRgbDisplayProfile(header)) continue;
                var name = System.IO.Path.GetFileNameWithoutExtension(path);
                bool srgb = name.Equals("sRGB Color Space Profile", StringComparison.OrdinalIgnoreCase);
                result.Add(new(srgb ? "标准 sRGB · 系统内置" : name, path));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return result;
    }
    internal static bool IsRgbDisplayProfile(ReadOnlySpan<byte> b) => b.Length >= 128 && b.Slice(36, 4).SequenceEqual("acsp"u8) &&
        b.Slice(16, 4).SequenceEqual("RGB "u8) && (b.Slice(12, 4).SequenceEqual("mntr"u8) || b.Slice(12, 4).SequenceEqual("spac"u8));

    public static string Apply(DisplayInspection display, DisplayProfileOption selected)
    {
        if (display.AdvancedColorActive != false) throw new InvalidOperationException("当前为 HDR / 高级颜色模式，或颜色状态未知；此处的 SDR 配置不能用于该模式。");
        string device = display.ColorDeviceKey;
        if (string.IsNullOrWhiteSpace(device)) throw new InvalidOperationException("未获得显示器颜色管理标识，请刷新后重试。");
        string path = selected.Path;
        if (selected.IsNative)
        {
            if (display.WhitePoint is not { } white) throw new InvalidOperationException("显示器没有提供白点数据。");
            double gamma = double.TryParse(display.Edid.Gamma, out double value) ? value : 2.2;
            var bytes = DisplayIccProfile.CreateNative(display.Primaries, white, gamma);
            string characterization = display.InterfaceId + "|v1|" + string.Join("|", display.Primaries.SelectMany(p => new[] { p.X, p.Y }).Concat(new[] { white.X, white.Y, gamma }).Select(v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));
            string filename = "TubaDisplay_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(characterization)))[..16] + ".icc";
            string generated = System.IO.Path.Combine(ConfigManager.GetDataDir(), "DisplayProfiles"); Directory.CreateDirectory(generated);
            path = System.IO.Path.Combine(generated, filename); File.WriteAllBytes(path, bytes);
            string installed = System.IO.Path.Combine(DirectoryPath, filename);
            if (!File.Exists(installed) && !InstallColorProfileW(null, path)) throw Error("安装自动生成的原生配置失败");
            path = installed;
        }
        var fullPath = System.IO.Path.GetFullPath(path);
        if (!string.Equals(System.IO.Path.GetDirectoryName(fullPath), DirectoryPath.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("只能应用系统中已安装的显示器颜色配置。");
        using (var file = File.OpenRead(fullPath))
        {
            var header = new byte[128]; file.ReadExactly(header);
            if (!IsRgbDisplayProfile(header)) throw new InvalidOperationException("此文件不是有效的 RGB 显示器配置。");
        }
        lock (Backups)
        {
            if (!WcsGetUsePerUserProfiles(device, MonitorClass, out bool perUser)) throw Error("读取颜色管理范围失败");
            var previous = ReadDefault(device);
            if (perUser && string.IsNullOrEmpty(previous)) previous = System.IO.Path.GetFileName(display.ProfilePath);
            if (perUser && string.IsNullOrEmpty(previous)) throw new InvalidOperationException("无法备份当前颜色配置，因此没有修改设置。");
            var before = new Backup(perUser, previous);
            try
            {
                if (!WcsSetUsePerUserProfiles(device, MonitorClass, true)) throw Error("启用当前用户颜色设置失败");
                string name = System.IO.Path.GetFileName(fullPath);
                if (!WcsAssociateColorProfileWithDevice(CurrentUser, name, device)) throw Error("关联颜色配置失败");
                if (!WcsSetDefaultColorProfile(CurrentUser, device, Icc, None, 0, name)) throw Error("应用颜色配置失败");
                string? actual = ReadDefault(device);
                if (!string.Equals(System.IO.Path.GetFileName(actual), name, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Windows 未确认默认颜色配置，已尝试恢复。");
                Backups.TryAdd(device, before);
                return fullPath;
            }
            catch
            {
                RestoreCore(device, before);
                throw;
            }
        }
    }
    public static void Restore(DisplayInspection display)
    {
        lock (Backups)
        {
            if (!Backups.TryGetValue(display.ColorDeviceKey, out var backup)) return;
            RestoreCore(display.ColorDeviceKey, backup); Backups.Remove(display.ColorDeviceKey);
        }
    }
    private static void RestoreCore(string device, Backup backup)
    {
        // A device without per-user overrides has no user default to set. Some Windows builds
        // reject a null name despite the legacy API documentation; restoring system scope suffices.
        if (backup.PerUser && !WcsSetDefaultColorProfile(CurrentUser, device, Icc, None, 0, backup.UserDefault)) throw Error("恢复原始颜色配置失败");
        if (!WcsSetUsePerUserProfiles(device, MonitorClass, backup.PerUser)) throw Error("恢复原始颜色管理范围失败");
    }
    private static string? ReadDefault(string device)
    {
        if (!WcsGetDefaultColorProfileSize(CurrentUser, device, Icc, None, 0, out uint bytes))
        {
            int code = Marshal.GetLastWin32Error();
            if (code is 2 or 1168) return null;
            throw new Win32Exception(code, "读取默认颜色配置失败");
        }
        if (bytes == 0) return null;
        if (bytes > 65536) throw new InvalidOperationException("默认颜色配置名称过长。");
        var name = new StringBuilder((int)bytes / 2);
        if (!WcsGetDefaultColorProfile(CurrentUser, device, Icc, None, 0, bytes, name)) throw Error("读取默认颜色配置失败");
        return name.ToString();
    }
    private static Win32Exception Error(string message)
    {
        int code = Marshal.GetLastWin32Error(); return new(code, $"{message}（Windows 错误 {code}）");
    }
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool GetColorDirectoryW(string? machine, StringBuilder buffer, ref uint size);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool InstallColorProfileW(string? machine, string filename);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool WcsGetUsePerUserProfiles(string device, uint deviceClass, out bool usePerUser);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool WcsSetUsePerUserProfiles(string device, uint deviceClass, bool usePerUser);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool WcsAssociateColorProfileWithDevice(uint scope, string profileName, string device);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool WcsSetDefaultColorProfile(uint scope, string device, uint type, uint subtype, uint id, string? name);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool WcsGetDefaultColorProfileSize(uint scope, string device, uint type, uint subtype, uint id, out uint bytes);
    [DllImport("mscms.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool WcsGetDefaultColorProfile(uint scope, string device, uint type, uint subtype, uint id, uint bytes, StringBuilder name);
}
