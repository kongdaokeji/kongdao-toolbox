using System.Diagnostics;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

public sealed class CpuzInfo
{
    public string? CpuName;
    public string? CpuCodeName;
    public string? CpuPackage;
    public int CpuCores;
    public int CpuThreads;

    public string? BoardManufacturer;
    public string? BoardModel;
    public string? BoardChipset;
    public string? BiosBrand;
    public string? BiosVersion;

    public string? MemoryType;
    public string? MemorySize;
    public string? MemorySpeed;
    public string? MemoryChannel;
    public List<CpuzMemDevice> MemDevices = [];

    public List<CpuzGpu> Gpus = [];
}

public sealed class CpuzGpu
{
    public string? Name;
    public string? GpuCode;
    public string? MemorySize;
    public string? MemoryType;
    public string? MemoryBus;
    public string? DriverVersion;
    public string? DeviceId;
}

public sealed class CpuzMemDevice
{
    public string? Designation;
    public string? Type;
    public string? Size;
    public string? Speed;
    public string? Manufacturer;
    public string? PartNumber;
}

public static class CpuzInfoService
{
    private static CpuzInfo? _cachedInfo;
    private static readonly object _lock = new();

    public static CpuzInfo? CachedInfo
    {
        get { lock (_lock) { return _cachedInfo; } }
    }

    public static void InvalidateCache()
    {
        lock (_lock) { _cachedInfo = null; }
    }

    public static string? FindCpuzExe()
    {
        var toolsRoot = ToolCatalog.ToolsRoot;
        if (toolsRoot is null) return null;

        var cpuzDir = Path.Combine(toolsRoot, "处理器工具", "CPUZ");
        if (!Directory.Exists(cpuzDir)) return null;

        // Use OSArchitecture (not ProcessArchitecture) so an x86/x64 build of this app
        // running on ARM64 Windows still picks the native ARM64 CPU-Z.
        if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
        {
            var arm64 = Path.Combine(cpuzDir, "cpuz_arm64.exe");
            if (File.Exists(arm64)) return arm64;
        }

        if (RuntimeInformation.OSArchitecture == Architecture.X86)
        {
            var x32 = Path.Combine(cpuzDir, "cpuz_x32.exe");
            if (File.Exists(x32)) return x32;
        }

        var x64 = Path.Combine(cpuzDir, "cpuz_x64.exe");
        if (File.Exists(x64)) return x64;

        var x32Fallback = Path.Combine(cpuzDir, "cpuz_x32.exe");
        if (File.Exists(x32Fallback)) return x32Fallback;

        var arm64Fallback = Path.Combine(cpuzDir, "cpuz_arm64.exe");
        if (File.Exists(arm64Fallback)) return arm64Fallback;

        return null;
    }

    public static async Task<CpuzInfo?> FetchAsync(int timeoutMs = 30000)
    {
        var exePath = FindCpuzExe();
        if (exePath is null) return null;

        var reportName = "cpuz_report_" + Environment.TickCount64;
        var exeDir = Path.GetDirectoryName(exePath)!;
        var reportPath = Path.Combine(exeDir, reportName + ".txt");

        try
        {
            CleanOldReports(exeDir);

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "-txt=" + reportName,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden,
                WorkingDirectory = exeDir
            };

            using var proc = Process.Start(psi);
            if (proc is null) return null;

            var cts = new CancellationTokenSource(timeoutMs);
            try
            {
                await proc.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                try { proc.Kill(); } catch { }
                return null;
            }

            var maxWait = 5000;
            var waited = 0;
            while (!File.Exists(reportPath) && waited < maxWait)
            {
                await Task.Delay(300);
                waited += 300;
            }

            if (!File.Exists(reportPath)) return null;

            await Task.Delay(500);

            string content;
            try
            {
                content = await File.ReadAllTextAsync(reportPath);
            }
            catch
            {
                return null;
            }

            var info = ParseReport(content);
            lock (_lock) { _cachedInfo = info; }
            return info;
        }
        finally
        {
            try { if (File.Exists(reportPath)) File.Delete(reportPath); } catch { }
            KillCpuzProcesses();
        }
    }

    private static void CleanOldReports(string dir)
    {
        try
        {
            foreach (var f in Directory.GetFiles(dir, "cpuz_report_*.txt"))
            {
                try { File.Delete(f); } catch { }
            }
        }
        catch { }
    }

    public static void KillCpuzProcesses()
    {
        try
        {
            foreach (var n in new[] { "cpuz_x64", "cpuz_x32", "cpuz_arm64", "cpuz" })
                foreach (var p in Process.GetProcessesByName(n))
                    try { p.Kill(); } catch { }
        }
        catch { }
    }

    internal static CpuzInfo ParseReport(string content)
    {
        var info = new CpuzInfo();
        var lines = content.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();

            if (trimmed == "Processors Information")
                i = ParseSection(info, lines, i, ParseCpuLine);
            else if (trimmed == "Chipset")
                i = ParseSection(info, lines, i, ParseChipsetLine);
            else if (trimmed.StartsWith("DMI BIOS"))
                i = ParseDmiSection(info, lines, i, ParseDmiBiosLine);
            else if (trimmed.StartsWith("DMI Baseboard"))
                i = ParseDmiSection(info, lines, i, ParseDmiBoardLine);
            else if (trimmed.StartsWith("DMI Memory Device"))
                i = ParseDmiMemDevice(info, lines, i);
            else if (trimmed == "Display Adapters")
                i = ParseGpuSection(info, lines, i);
        }

        return info;
    }

    private static int ParseSection(CpuzInfo info, string[] lines, int start, Action<CpuzInfo, string, string> parser)
    {
        for (var i = start + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (IsNextSection(lines, i)) return i - 1;
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var kv = ParseTabLine(trimmed);
            if (kv is not null) parser(info, kv.Value.Key, kv.Value.Value);
        }
        return lines.Length - 1;
    }

    private static int ParseDmiSection(CpuzInfo info, string[] lines, int start, Action<CpuzInfo, string, string> parser)
    {
        for (var i = start + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("DMI ") && i > start + 1) return i - 1;
            if (string.IsNullOrWhiteSpace(trimmed)) continue;

            var kv = ParseTabLine(trimmed);
            if (kv is not null) parser(info, kv.Value.Key, kv.Value.Value);
        }
        return lines.Length - 1;
    }

    private static int ParseDmiMemDevice(CpuzInfo info, string[] lines, int start)
    {
        var dev = new CpuzMemDevice();
        var gotData = false;

        for (var i = start + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("DMI ") && i > start + 1)
            {
                if (gotData) info.MemDevices.Add(dev);
                return i - 1;
            }
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (gotData) { info.MemDevices.Add(dev); dev = new CpuzMemDevice(); gotData = false; }
                continue;
            }

            var kv = ParseTabLine(trimmed);
            if (kv is null) continue;
            gotData = true;

            var (key, val) = kv.Value;
            var ku = key.ToUpperInvariant();
            if (ku == "DESIGNATION") dev.Designation = val;
            else if (ku == "TYPE") dev.Type = val;
            else if (ku == "SIZE") dev.Size = val;
            else if (ku == "SPEED") dev.Speed = val;
            else if (ku == "MANUFACTURER") dev.Manufacturer = val;
            else if (ku == "PART NUMBER") dev.PartNumber = val?.Trim();
        }

        if (gotData) info.MemDevices.Add(dev);
        return lines.Length - 1;
    }

    private static void ParseCpuLine(CpuzInfo info, string key, string val)
    {
        var ku = key.ToUpperInvariant();
        if (ku == "NAME" && string.IsNullOrWhiteSpace(info.CpuName)) info.CpuName = val;
        else if (ku == "SPECIFICATION" && string.IsNullOrWhiteSpace(info.CpuName)) info.CpuName = val;
        else if (ku == "CODENAME") info.CpuCodeName = val;
        else if (ku.StartsWith("PACKAGE")) info.CpuPackage = val;
        else if (ku.Contains("NUMBER OF CORES"))
        {
            if (int.TryParse(val.Split('(')[0].Trim(), out var c)) info.CpuCores = c;
        }
        else if (ku.Contains("NUMBER OF THREADS"))
        {
            if (int.TryParse(val.Split('(')[0].Trim(), out var t)) info.CpuThreads = t;
        }
    }

    private static void ParseChipsetLine(CpuzInfo info, string key, string val)
    {
        var ku = key.ToUpperInvariant();
        if (ku == "NORTHBRIDGE" && string.IsNullOrWhiteSpace(info.BoardChipset)) info.BoardChipset = val;
        else if (ku == "MEMORY TYPE") info.MemoryType = val;
        else if (ku == "MEMORY SIZE") info.MemorySize = val;
        else if (ku == "MEMORY FREQUENCY") info.MemorySpeed = val;
        else if (ku == "CHANNELS") info.MemoryChannel = val;
    }

    private static void ParseDmiBiosLine(CpuzInfo info, string key, string val)
    {
        var ku = key.ToUpperInvariant();
        if (ku == "VENDOR") info.BiosBrand = val;
        else if (ku == "VERSION") info.BiosVersion = val;
    }

    private static void ParseDmiBoardLine(CpuzInfo info, string key, string val)
    {
        var ku = key.ToUpperInvariant();
        if (ku == "VENDOR") info.BoardManufacturer = val;
        else if (ku == "MODEL") info.BoardModel = val;
    }

    private static int ParseGpuSection(CpuzInfo info, string[] lines, int start)
    {
        var gpu = new CpuzGpu();
        var gotData = false;

        for (var i = start + 1; i < lines.Length; i++)
        {
            var trimmed = lines[i].Trim();
            if (IsNextSection(lines, i))
            {
                if (gotData) info.Gpus.Add(gpu);
                return i - 1;
            }
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                if (gotData) { info.Gpus.Add(gpu); gpu = new CpuzGpu(); gotData = false; }
                continue;
            }

            var kv = ParseTabLine(trimmed);
            if (kv is null) continue;
            gotData = true;

            var (key, val) = kv.Value;
            var ku = key.ToUpperInvariant();
            if (ku == "NAME" && string.IsNullOrWhiteSpace(gpu.Name)) gpu.Name = val;
            else if (ku == "GPU") gpu.GpuCode = val;
            else if (ku == "MEMORY SIZE") gpu.MemorySize = val;
            else if (ku == "MEMORY TYPE") gpu.MemoryType = val;
            else if (ku == "MEMORY BUS WIDTH") gpu.MemoryBus = val;
            else if (ku == "DRIVER VERSION") gpu.DriverVersion = val;
            else if (ku == "DEVICE ID") gpu.DeviceId = val;
        }

        if (gotData) info.Gpus.Add(gpu);
        return lines.Length - 1;
    }

    private static bool IsNextSection(string[] lines, int i)
    {
        if (i + 1 >= lines.Length) return true;
        var next = lines[i + 1].Trim();
        return next.StartsWith("---");
    }

    internal static (string Key, string Value)? ParseTabLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;

        var tabIdx = line.IndexOf('\t');
        if (tabIdx < 0) return null;

        var key = line[..tabIdx].Trim();
        if (string.IsNullOrWhiteSpace(key)) return null;

        var valStart = tabIdx;
        while (valStart < line.Length && line[valStart] == '\t') valStart++;

        var val = valStart < line.Length ? line[valStart..].Trim() : "";
        if (string.IsNullOrWhiteSpace(val)) return null;

        return (key, val);
    }
}