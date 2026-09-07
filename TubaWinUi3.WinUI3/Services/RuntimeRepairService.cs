using System.Diagnostics;
using System.Net.Http;
using Microsoft.Win32;

namespace TubaWinUi3.Services;

/// <summary>
/// 运行库修复：注册表 + 文件系统检测 VC++ / .NET Framework / DirectX 旧版组件的缺失或不完整项，
/// 从微软官方 URL 下载对应安装包，校验 Microsoft Authenticode 签名后静默安装。
/// 判定与安装流程移植自 NexBox runtime_repair.rs（CrystalDiskInfo 同源判定思路不涉及，纯注册表/文件检测）。
/// </summary>
public static class RuntimeRepairService
{
    public const string VisualCppId = "visual-cpp";
    public const string DotNetId = "dotnet";
    public const string DirectXId = "directx";

    public const string PhaseDownloading = "downloading";
    public const string PhaseVerifying = "verifying";
    public const string PhaseInstalling = "installing";
    public const string PhaseComplete = "complete";

    // Microsoft 官方安装包地址（aka.ms 短链 / download.microsoft.com 直链）
    private const string VcX64Url = "https://aka.ms/vc14/vc_redist.x64.exe";
    private const string VcX86Url = "https://aka.ms/vc14/vc_redist.x86.exe";
    private const string Vc2013X64Url = "https://aka.ms/highdpimfc2013x64enu";
    private const string Vc2013X86Url = "https://aka.ms/highdpimfc2013x86enu";
    private const string Vc2012X64Url = "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x64.exe";
    private const string Vc2012X86Url = "https://download.microsoft.com/download/1/6/B/16B06F60-3B20-4FF2-B699-5E9B7962F9AE/VSU_4/vcredist_x86.exe";
    private const string Vc2010X64Url = "https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x64.exe";
    private const string Vc2010X86Url = "https://download.microsoft.com/download/1/6/5/165255E7-1014-4D0A-B094-B6A430A6BFFC/vcredist_x86.exe";
    private const string Vc2008X64Url = "https://download.microsoft.com/download/5/D/8/5D8C65CB-C849-4025-8E95-C3966CAFD8AE/vcredist_x64.exe";
    private const string Vc2008X86Url = "https://download.microsoft.com/download/5/D/8/5D8C65CB-C849-4025-8E95-C3966CAFD8AE/vcredist_x86.exe";
    private const string DotNet481Url = "https://go.microsoft.com/fwlink/?linkid=2203305";
    private const string DirectXUrl = "https://download.microsoft.com/download/1/7/1/1718ccc4-6315-4d8e-9543-8e28a4e18c4c/dxwebsetup.exe";

    // v14 (2015-2026) 完整运行时 DLL 清单
    private static readonly string[] V14Files =
    [
        "vcruntime140.dll", "msvcp140.dll", "msvcp140_1.dll", "msvcp140_2.dll",
        "concrt140.dll", "vcomp140.dll", "ucrtbase.dll",
    ];

    // DirectX 旧版游戏兼容可再发行 DLL
    private static readonly string[] DirectxDlls =
    [
        "d3dx9_43.dll", "d3dx10_43.dll", "d3dx11_43.dll", "d3dcompiler_43.dll", "d3dcsx_43.dll",
        "xinput1_3.dll", "xaudio2_7.dll", "x3daudio1_7.dll", "xapofx1_5.dll", "xactengine3_7.dll",
    ];

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate })
        {
            Timeout = TimeSpan.FromMinutes(15),
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TubaWinUi3 RuntimeRepair/1.0");
        return client;
    }

    public static Task<IReadOnlyList<RuntimeStatus>> DetectAsync() => Task.Run(Detect);

    /// <summary>对指定运行库执行修复（下载 → 校验签名 → 静默安装），返回结果消息。</summary>
    public static async Task<string> RepairAsync(
        string runtimeId,
        Action<RuntimeRepairProgress>? progress = null,
        CancellationToken ct = default)
    {
        var statuses = await DetectAsync().ConfigureAwait(false);
        var status = statuses.FirstOrDefault(s => s.Id == runtimeId)
            ?? throw new InvalidOperationException("未找到运行库检测结果");
        if (status.Installed)
            throw new InvalidOperationException("检测结果显示该运行库完整，无需修复");

        var packages = BuildPackages(runtimeId, status.MissingComponents);
        var cacheDir = Path.Combine(ConfigManager.GetDataDir(), "RuntimeRepair");
        Directory.CreateDirectory(cacheDir);

        var count = packages.Count;
        var restartRequired = false;
        for (var i = 0; i < packages.Count; i++)
        {
            var package = packages[i];
            var basePct = i * 100 / count;
            var span = 100 / count;
            var destination = Path.Combine(cacheDir, package.FileName);

            progress?.Invoke(new RuntimeRepairProgress { RuntimeId = runtimeId, Phase = PhaseDownloading, Percent = basePct, Detail = package.FileName });
            await DownloadPackageAsync(package, destination, runtimeId, basePct, Math.Max(span - 8, 0), progress, ct).ConfigureAwait(false);

            progress?.Invoke(new RuntimeRepairProgress { RuntimeId = runtimeId, Phase = PhaseVerifying, Percent = Math.Min(basePct + Math.Max(span - 7, 0), 100), Detail = package.FileName });
            await Task.Run(() => VerifyMicrosoftSignature(destination), ct).ConfigureAwait(false);

            progress?.Invoke(new RuntimeRepairProgress { RuntimeId = runtimeId, Phase = PhaseInstalling, Percent = Math.Min(basePct + Math.Max(span - 4, 0), 100), Detail = package.FileName });
            restartRequired |= await Task.Run(() => RunInstaller(destination, package.Args), ct).ConfigureAwait(false);
        }

        progress?.Invoke(new RuntimeRepairProgress { RuntimeId = runtimeId, Phase = PhaseComplete, Percent = 100, Detail = "完成" });
        return restartRequired ? "修复完成，系统需要重启后才能完全生效" : "修复完成";
    }

    // ─────────────────────────── 检测层 ───────────────────────────

    public static IReadOnlyList<RuntimeStatus> Detect()
    {
        var windows = WindowsDir();
        var system32 = Path.Combine(windows, "System32");
        var syswow64 = Path.Combine(windows, "SysWOW64");
        var is64Bit = Directory.Exists(syswow64);

        // ── Visual C++ ──
        var vcMissing = new List<string>();
        if (!HasVisualCppRedist("x64") && is64Bit)
            vcMissing.Add("未检测到 Visual C++ v14 x64 注册项");
        if (!HasVisualCppRedist("x86"))
            vcMissing.Add("未检测到 Visual C++ v14 x86 注册项");
        AddV14MissingFiles(vcMissing, system32, is64Bit ? "System32 (x64)" : "System32 (x86)");
        if (is64Bit)
            AddV14MissingFiles(vcMissing, syswow64, "SysWOW64 (x86)");

        foreach (var version in new[] { "2013", "2012", "2010", "2008" })
        {
            if (is64Bit && !HasLegacyVisualCppRedist(version, "x64"))
                vcMissing.Add($"未检测到 Visual C++ {version} x64 运行库");
            if (!HasLegacyVisualCppRedist(version, "x86"))
                vcMissing.Add($"未检测到 Visual C++ {version} x86 运行库");
        }
        foreach (var (version, files) in new[]
                 {
                     ("2013", new[] { "msvcr120.dll", "msvcp120.dll" }),
                     ("2012", new[] { "msvcr110.dll", "msvcp110.dll" }),
                     ("2010", new[] { "msvcr100.dll", "msvcp100.dll" }),
                 })
        {
            if (is64Bit)
            {
                AddLegacyMissingFiles(vcMissing, version, "x64", system32, "System32 (x64)", files);
                AddLegacyMissingFiles(vcMissing, version, "x86", syswow64, "SysWOW64 (x86)", files);
            }
            else
            {
                AddLegacyMissingFiles(vcMissing, version, "x86", system32, "System32 (x86)", files);
            }
        }
        foreach (var architecture in is64Bit ? new[] { "x64", "x86" } : new[] { "x86" })
        {
            foreach (var file in new[] { "msvcr90.dll", "msvcp90.dll" })
            {
                if (!HasVc2008WinSxSFile(windows, architecture, file))
                    vcMissing.Add($"缺少 Visual C++ 2008 {architecture} WinSxS\\{file}");
            }
        }

        // ── .NET Framework ──
        var netRelease = 0;
        using (var netKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
            netRelease = netKey?.GetValue("Release") as int? ?? 0;
        var dotnetMissing = new List<string>();
        if (netRelease < 533_320)
            dotnetMissing.Add("未检测到 .NET Framework 4.8.1 Runtime");
        AddMissingFile(dotnetMissing, "缺少 Framework\\v4.0.30319\\mscorlib.dll",
            Path.Combine(windows, "Microsoft.NET", "Framework", "v4.0.30319", "mscorlib.dll"));
        if (is64Bit)
            AddMissingFile(dotnetMissing, "缺少 Framework64\\v4.0.30319\\mscorlib.dll",
                Path.Combine(windows, "Microsoft.NET", "Framework64", "v4.0.30319", "mscorlib.dll"));

        // ── DirectX ──
        var directxMissing = new List<string>();
        foreach (var file in DirectxDlls)
        {
            AddMissingFile(directxMissing, $"缺少 System32\\{file}", Path.Combine(system32, file));
            if (is64Bit)
                AddMissingFile(directxMissing, $"缺少 SysWOW64\\{file}", Path.Combine(syswow64, file));
        }

        return
        [
            new RuntimeStatus
            {
                Id = VisualCppId,
                Name = "Microsoft Visual C++ 2008-2026",
                Installed = vcMissing.Count == 0,
                Summary = vcMissing.Count == 0
                    ? "Visual C++ 2008-2026 x86/x64 游戏运行库完整"
                    : $"检测到 {vcMissing.Count} 项缺失",
                MissingComponents = vcMissing,
            },
            new RuntimeStatus
            {
                Id = DotNetId,
                Name = ".NET Framework",
                Installed = dotnetMissing.Count == 0,
                Summary = dotnetMissing.Count == 0
                    ? $".NET Framework 4.8.1 Runtime 已安装 (Release {netRelease})"
                    : $"检测到 {dotnetMissing.Count} 项缺失",
                MissingComponents = dotnetMissing,
            },
            new RuntimeStatus
            {
                Id = DirectXId,
                Name = "DirectX 9-12 游戏组件",
                Installed = directxMissing.Count == 0,
                Summary = directxMissing.Count == 0
                    ? "DirectX 旧版游戏兼容组件完整"
                    : $"检测到 {directxMissing.Count} 个 DirectX 兼容 DLL 缺失",
                MissingComponents = directxMissing,
            },
        ];
    }

    private static string WindowsDir() =>
        Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows";

    private static void AddMissingFile(List<string> missing, string label, string path)
    {
        if (!File.Exists(path))
            missing.Add(label);
    }

    private static void AddV14MissingFiles(List<string> missing, string directory, string directoryLabel)
    {
        foreach (var file in V14Files)
            AddMissingFile(missing, $"缺少 {directoryLabel}\\{file}", Path.Combine(directory, file));
    }

    private static void AddLegacyMissingFiles(List<string> missing, string version, string architecture,
        string directory, string directoryLabel, string[] files)
    {
        foreach (var file in files)
            AddMissingFile(missing, $"缺少 Visual C++ {version} {architecture} {directoryLabel}\\{file}",
                Path.Combine(directory, file));
    }

    /// <summary>v14（2015-2026）通过 Installer\Dependencies 的 vc,redist.x64/x86 前缀或卸载表 DisplayName 判定。</summary>
    private static bool HasVisualCppRedist(string architecture)
    {
        var prefix = architecture == "x64" ? "vc,redist.x64" : "vc,redist.x86";
        // 注册表项名大小写不固定（如 "VC,redist.x64,amd64,14.50,bundle"），归一化后比较
        if (EnumHklmSubKeys(@"SOFTWARE\Classes\Installer\Dependencies")
            .Any(name => name.ToLowerInvariant().StartsWith(prefix, StringComparison.Ordinal)))
            return true;

        return UninstallDisplayNames().Any(displayName =>
        {
            var normalized = displayName.ToLowerInvariant();
            return normalized.Contains("microsoft visual c++", StringComparison.Ordinal)
                && (normalized.Contains("v14 redistributable", StringComparison.Ordinal)
                    || normalized.Contains("2022", StringComparison.Ordinal)
                    || normalized.Contains("2015-2019", StringComparison.Ordinal)
                    || normalized.Contains("2015-2022", StringComparison.Ordinal)
                    || normalized.Contains("2015-2026", StringComparison.Ordinal))
                && normalized.Contains(architecture, StringComparison.Ordinal);
        });
    }

    /// <summary>旧版 VC++（2013/2012/2010/2008）：卸载表必须有 (x64)/(x86) 标记的 DisplayName。</summary>
    private static bool HasLegacyVisualCppRedist(string version, string architecture)
    {
        var names = UninstallDisplayNames()
            .Select(n => n.ToLowerInvariant())
            .Where(n => n.Contains("microsoft visual c++", StringComparison.Ordinal)
                && n.Contains(version, StringComparison.Ordinal)
                && (n.Contains($"({architecture})", StringComparison.Ordinal)
                    || n.Contains($" {architecture} ", StringComparison.Ordinal)))
            .ToList();

        var hasAdditional = names.Any(n => n.Contains("additional runtime", StringComparison.Ordinal));
        var hasMinimum = names.Any(n => n.Contains("minimum runtime", StringComparison.Ordinal));
        return (hasAdditional && hasMinimum)
            || names.Any(n => n.Contains("redistributable", StringComparison.Ordinal) && !n.Contains("runtime", StringComparison.Ordinal));
    }

    /// <summary>VC2008 的 DLL 装在 WinSxS 的 vc90.crt 目录中：读取该目录并检查文件。</summary>
    private static bool HasVc2008WinSxSFile(string windows, string architecture, string file)
    {
        var prefix = architecture == "x64" ? "amd64_microsoft.vc90.crt_" : "x86_microsoft.vc90.crt_";
        var winsxs = Path.Combine(windows, "WinSxS");
        if (!Directory.Exists(winsxs))
            return false;
        return Directory.EnumerateDirectories(winsxs).Any(entry =>
        {
            var name = Path.GetFileName(entry).ToLowerInvariant();
            return name.StartsWith(prefix, StringComparison.Ordinal) && File.Exists(Path.Combine(entry, file));
        });
    }

    private static IEnumerable<string> EnumHklmSubKeys(string path)
    {
        using var key = Registry.LocalMachine.OpenSubKey(path);
        if (key is null)
            yield break;
        foreach (var name in key.GetSubKeyNames())
            yield return name;
    }

    /// <summary>两个卸载表路径下的子键名。</summary>
    private static IEnumerable<string> UninstallKeyNames()
    {
        foreach (var path in new[]
                 {
                     @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                     @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                 })
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key is null)
                continue;
            foreach (var sub in key.GetSubKeyNames())
                yield return sub;
        }
    }

    /// <summary>两个卸载表路径下的 DisplayName 值。</summary>
    private static IEnumerable<string> UninstallDisplayNames()
    {
        foreach (var path in new[]
                 {
                     @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
                     @"SOFTWARE\Wow6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
                 })
        {
            using var key = Registry.LocalMachine.OpenSubKey(path);
            if (key is null)
                continue;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var subKey = key.OpenSubKey(sub);
                if (subKey?.GetValue("DisplayName") is string name && name.Length > 0)
                    yield return name;
            }
        }
    }

    // ─────────────────────────── 修复层 ───────────────────────────

    internal sealed record RuntimePackage(string FileName, string Url, string[] Args);

    /// <summary>把缺失项映射为需要下载安装的微软官方安装包（缺失项 → 包）。</summary>
    internal static List<RuntimePackage> BuildPackages(string runtimeId, IReadOnlyList<string> missing)
    {
        switch (runtimeId)
        {
            case VisualCppId:
            {
                bool Needs(string version, string architecture) =>
                    missing.Any(c => c.Contains($"Visual C++ {version} {architecture}", StringComparison.Ordinal));
                // 未检测到注册项 → 全新安装；检测到但 DLL 缺失 → 走 /repair 修复安装
                bool NeedsRepair(string version, string architecture) =>
                    !missing.Any(c => c == $"未检测到 Visual C++ {version} {architecture} 运行库");

                var needsV14X64 = missing.Any(c => c.Contains("Visual C++ v14 x64", StringComparison.Ordinal)
                    || V14Files.Any(f => c.Contains($"System32 (x64)\\{f}", StringComparison.Ordinal)));
                var needsV14X86 = missing.Any(c => c.Contains("Visual C++ v14 x86", StringComparison.Ordinal)
                    || V14Files.Any(f => c.Contains($"SysWOW64 (x86)\\{f}", StringComparison.Ordinal)
                        || c.Contains($"System32 (x86)\\{f}", StringComparison.Ordinal)));
                var repairV14X64 = missing.Any(c => c.Contains("System32 (x64)", StringComparison.Ordinal));
                var repairV14X86 = missing.Any(c => c.Contains("SysWOW64 (x86)", StringComparison.Ordinal)
                    || c.Contains("System32 (x86)", StringComparison.Ordinal));

                var packages = new List<RuntimePackage>();
                if (Needs("2013", "x64"))
                    packages.Add(new RuntimePackage("vc2013_x64.exe", Vc2013X64Url,
                        NeedsRepair("2013", "x64") ? ["/repair", "/quiet", "/norestart"] : ["/install", "/quiet", "/norestart"]));
                if (Needs("2013", "x86"))
                    packages.Add(new RuntimePackage("vc2013_x86.exe", Vc2013X86Url,
                        NeedsRepair("2013", "x86") ? ["/repair", "/quiet", "/norestart"] : ["/install", "/quiet", "/norestart"]));
                if (Needs("2012", "x64"))
                    packages.Add(new RuntimePackage("vc2012_x64.exe", Vc2012X64Url,
                        NeedsRepair("2012", "x64") ? ["/repair", "/quiet", "/norestart"] : ["/install", "/quiet", "/norestart"]));
                if (Needs("2012", "x86"))
                    packages.Add(new RuntimePackage("vc2012_x86.exe", Vc2012X86Url,
                        NeedsRepair("2012", "x86") ? ["/repair", "/quiet", "/norestart"] : ["/install", "/quiet", "/norestart"]));
                if (Needs("2010", "x64"))
                    packages.Add(new RuntimePackage("vc2010_x64.exe", Vc2010X64Url,
                        NeedsRepair("2010", "x64") ? ["/repair", "/quiet", "/norestart"] : ["/q", "/norestart"]));
                if (Needs("2010", "x86"))
                    packages.Add(new RuntimePackage("vc2010_x86.exe", Vc2010X86Url,
                        NeedsRepair("2010", "x86") ? ["/repair", "/quiet", "/norestart"] : ["/q", "/norestart"]));
                if (Needs("2008", "x64"))
                    packages.Add(new RuntimePackage("vc2008_x64.exe", Vc2008X64Url,
                        NeedsRepair("2008", "x64") ? ["/repair", "/quiet", "/norestart"] : ["/q", "/norestart"]));
                if (Needs("2008", "x86"))
                    packages.Add(new RuntimePackage("vc2008_x86.exe", Vc2008X86Url,
                        NeedsRepair("2008", "x86") ? ["/repair", "/quiet", "/norestart"] : ["/q", "/norestart"]));
                if (needsV14X64)
                    packages.Add(new RuntimePackage("vc14_x64.exe", VcX64Url,
                        repairV14X64 ? ["/repair", "/quiet", "/norestart"] : ["/install", "/quiet", "/norestart"]));
                if (needsV14X86)
                    packages.Add(new RuntimePackage("vc14_x86.exe", VcX86Url,
                        repairV14X86 ? ["/repair", "/quiet", "/norestart"] : ["/install", "/quiet", "/norestart"]));

                if (packages.Count == 0)
                    throw new InvalidOperationException("未找到可修复的 Visual C++ 缺失项");
                return packages;
            }
            case DotNetId:
                return [new RuntimePackage("ndp481-x86-x64-allos-enu.exe", DotNet481Url, ["/repair", "/quiet", "/norestart"])];
            case DirectXId:
                return [new RuntimePackage("dxwebsetup.exe", DirectXUrl, ["/Q"])];
            default:
                throw new InvalidOperationException("不支持的运行库修复项目");
        }
    }

    private static async Task DownloadPackageAsync(RuntimePackage package, string destination, string runtimeId,
        int basePct, int span, Action<RuntimeRepairProgress>? progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(package.Url, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        try
        {
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"下载 {package.FileName} 失败: {ex.Message}", ex);
        }

        var total = response.Content.Headers.ContentLength ?? 0;
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = File.Create(destination);
        var buffer = new byte[81920];
        long downloaded = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (read == 0)
                break;
            await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            downloaded += read;
            var completed = total == 0 ? 0 : (int)(downloaded * span / total);
            progress?.Invoke(new RuntimeRepairProgress
            {
                RuntimeId = runtimeId,
                Phase = PhaseDownloading,
                Percent = Math.Min(basePct + completed, 96),
                Detail = package.FileName,
            });
        }
        await file.FlushAsync(ct).ConfigureAwait(false);
    }

    /// <summary>用 PowerShell Get-AuthenticodeSignature 校验微软签名（与 NexBox 相同方案）。</summary>
    private static void VerifyMicrosoftSignature(string path)
    {
        var escaped = path.Replace("'", "''");
        var script =
            $"$signature = Get-AuthenticodeSignature -LiteralPath '{escaped}'; " +
            "if ($signature.Status -eq 'Valid' -and $signature.SignerCertificate.Subject -match 'Microsoft Corporation') { exit 0 }; " +
            "Write-Error ('签名校验失败: ' + $signature.Status); exit 1";

        var psi = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动签名校验进程");
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(stderr.Trim().Length > 0 ? stderr.Trim() : "签名校验失败");
    }

    /// <summary>静默运行安装程序；返回 true 表示需要重启（3010/1641）。</summary>
    private static bool RunInstaller(string path, string[] args)
    {
        var psi = new ProcessStartInfo(path) { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动安装程序");
        process.WaitForExit();
        return process.ExitCode switch
        {
            0 => false,
            3010 or 1641 => true,
            var code => throw new InvalidOperationException($"安装程序退出代码: {code}"),
        };
    }
}

/// <summary>某一运行库的安装状态。</summary>
public sealed class RuntimeStatus
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public bool Installed { get; init; }
    public required string Summary { get; init; }
    public IReadOnlyList<string> MissingComponents { get; init; } = [];
}

/// <summary>修复过程中的阶段进度（下载 / 校验 / 安装 / 完成）。</summary>
public sealed class RuntimeRepairProgress
{
    public required string RuntimeId { get; init; }
    public required string Phase { get; init; }
    public int Percent { get; init; }
    public required string Detail { get; init; }
}