using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.UI;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public enum DotnetComponentType
{
    Runtime,
    Sdk,
    AspNetCoreRuntime,
    WindowsDesktopRuntime,
    DotnetFramework
}

public enum DotnetInstallStatus
{
    NotInstalled,
    Installed,
    Installing,
    Downloading,
    Failed
}

public sealed class DotnetChannelInfo
{
    public string ChannelVersion { get; init; } = "";
    public string LatestRelease { get; init; } = "";
    public string LatestRuntime { get; init; } = "";
    public string LatestSdk { get; init; } = "";
    public string SupportPhase { get; init; } = "";
    public string ReleaseType { get; init; } = "";
    public string? EolDate { get; init; }
    public string ReleasesJsonUrl { get; init; } = "";
    public bool Security { get; init; }
}

public sealed class DotnetInstallableItem
{
    public DotnetComponentType ComponentType { get; init; }
    public string Version { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Rid { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string FileName { get; init; } = "";
    public string ChannelVersion { get; init; } = "";
    public DotnetInstallStatus Status { get; set; } = DotnetInstallStatus.NotInstalled;
    public double DownloadProgress { get; set; }
    public string? InstalledVersion { get; set; }
    public bool UseDism { get; init; }
}

public static class DotnetCompletionService
{
    private const string ReleasesIndexUrl = "https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/releases-index.json";

    private static readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly string _downloadDir = Path.Combine(ConfigManager.GetDataDir(), "DotnetDownloads");

    private static List<DotnetChannelInfo>? _channels;
    private static List<DotnetInstallableItem>? _installables;
    private static HashSet<string>? _installedRuntimes;
    private static HashSet<string>? _installedSdks;
    private static Dictionary<string, string>? _installedFrameworkVersions;
    private static string? _currentArch;

    public static List<DotnetChannelInfo> Channels => _channels ?? [];
    public static List<DotnetInstallableItem> Installables => _installables ?? [];
    public static bool IsLoaded => _channels is not null;

    public static event Action? DataChanged;

    public static string CurrentArch
    {
        get
        {
            if (_currentArch is not null) return _currentArch;
            _currentArch = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => "win-arm64",
                Architecture.X86 => "win-x86",
                _ => "win-x64"
            };
            return _currentArch;
        }
    }

    public static async Task LoadAsync()
    {
        await DetectInstalledAsync();
        await FetchChannelsAsync();
        await BuildInstallablesAsync();
        DataChanged?.Invoke();
    }

    public static async Task RefreshInstalledAsync()
    {
        await DetectInstalledAsync();
        if (_installables is not null)
        {
            foreach (var item in _installables)
            {
                if (item.Status is DotnetInstallStatus.Installing or DotnetInstallStatus.Downloading)
                    continue;
                UpdateItemStatus(item);
            }
            DataChanged?.Invoke();
        }
    }

    public static async Task DetectInstalledAsync()
    {
        var runtimes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var sdks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var frameworkVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        await Task.Run(() =>
        {
            var dotnetRoots = FindDotnetRoots();

            // 优先通过 .NET CLI 探测（权威来源）：dotnet --list-runtimes / --list-sdks
            var cliOk = RunDotnetCli(runtimes, sdks, dotnetRoots);

            // CLI 不可用（机器上没有 dotnet.exe / 未加入 PATH 且不在常见安装目录）时才退回目录扫描
            if (!cliOk)
                ScanDotnetInstallDirs(runtimes, sdks, dotnetRoots);

            DetectDotnetFrameworkFromRegistry(frameworkVersions);
        });

        _installedRuntimes = runtimes;
        _installedSdks = sdks;
        _installedFrameworkVersions = frameworkVersions;
    }

    private static List<string> FindDotnetRoots()
    {
        var roots = new List<string>();

        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot))
            roots.Add(dotnetRoot);

        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pfx86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var arch = RuntimeInformation.ProcessArchitecture;

        if (arch == Architecture.X86)
            roots.Add(Path.Combine(pfx86, "dotnet"));
        else
        {
            roots.Add(Path.Combine(pf, "dotnet"));
            roots.Add(Path.Combine(pfx86, "dotnet"));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        roots.Add(Path.Combine(localAppData, "Microsoft", "dotnet"));

        return roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static void ScanDotnetInstallDirs(HashSet<string> runtimes, HashSet<string> sdks, IReadOnlyList<string> roots)
    {
        void ScanPath(string? path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;
            try
            {
                var runtimeDir = Path.Combine(path, "shared");
                if (Directory.Exists(runtimeDir))
                {
                    foreach (var dir in Directory.EnumerateDirectories(runtimeDir))
                    {
                        var name = Path.GetFileName(dir);
                        foreach (var verDir in Directory.EnumerateDirectories(dir))
                            runtimes.Add($"{name}/{Path.GetFileName(verDir)}");
                    }
                }
                var sdkDir = Path.Combine(path, "sdk");
                if (Directory.Exists(sdkDir))
                {
                    foreach (var verDir in Directory.EnumerateDirectories(sdkDir))
                        sdks.Add(Path.GetFileName(verDir));
                }
            }
            catch { }
        }

        foreach (var root in roots)
            ScanPath(root);
    }

    /// <summary>
    /// 通过 .NET CLI 探测已安装组件。优先使用 PATH 中的 dotnet，
    /// 其次使用已知安装目录中的 dotnet.exe。CLI 成功执行即视为权威结果。
    /// </summary>
    private static bool RunDotnetCli(HashSet<string> runtimes, HashSet<string> sdks, IReadOnlyList<string> dotnetRoots)
    {
        var candidates = new List<string> { "dotnet" };
        foreach (var root in dotnetRoots)
        {
            var exe = Path.Combine(root, "dotnet.exe");
            if (File.Exists(exe) && !candidates.Contains(exe, StringComparer.OrdinalIgnoreCase))
                candidates.Add(exe);
        }

        foreach (var candidate in candidates)
        {
            try
            {
                var cliAvailable = false;

                if (TryRunDotnetList(candidate, "--list-runtimes", out var runtimeOutput))
                {
                    cliAvailable = true;
                    foreach (var line in runtimeOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var parts = line.Trim().Split(' ', 2);
                        if (parts.Length >= 2)
                            runtimes.Add($"{parts[0]}/{parts[1].Split(' ')[0]}");
                    }
                }

                if (TryRunDotnetList(candidate, "--list-sdks", out var sdkOutput))
                {
                    cliAvailable = true;
                    foreach (var line in sdkOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var sdkVer = line.Trim().Split(' ')[0];
                        if (sdkVer.Length > 0)
                            sdks.Add(sdkVer);
                    }
                }

                if (cliAvailable)
                    return true;
            }
            catch (Win32Exception)
            {
                // 该 dotnet 不存在或无法启动，尝试下一个候选
            }
        }

        return false;
    }

    private static bool TryRunDotnetList(string dotnetPath, string arguments, out string output)
    {
        output = "";
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = dotnetPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        proc.Start();
        output = proc.StandardOutput.ReadToEnd();
        proc.WaitForExit(5000);
        return proc.ExitCode == 0;
    }

    private static void DetectDotnetFrameworkFromRegistry(Dictionary<string, string> result)
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            if (key is not null)
            {
                var release = key.GetValue("Release");
                if (release is int releaseValue)
                {
                    var version = releaseValue switch
                    {
                        >= 533320 => "4.8.1",
                        >= 528040 => "4.8",
                        >= 461808 => "4.7.2",
                        >= 461308 => "4.7.1",
                        >= 460798 => "4.7",
                        >= 394802 => "4.6.2",
                        >= 394254 => "4.6.1",
                        >= 393295 => "4.6",
                        >= 379893 => "4.5.2",
                        >= 378675 => "4.5.1",
                        >= 378389 => "4.5",
                        _ => null
                    };
                    if (version is not null)
                        result["4.x"] = version;
                }
            }
        }
        catch { }

        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v3.5");
            if (key is not null)
            {
                var sp = key.GetValue("SP");
                if (sp is int spVal && spVal >= 1)
                    result["3.5"] = "3.5 SP1";
                else if (sp is int)
                    result["3.5"] = "3.5";
            }
        }
        catch { }
    }

    public static string? GetInstalledRuntimeVersion(string name, string channelVersion)
    {
        if (_installedRuntimes is null) return null;
        var prefix = $"{name}/";
        var channelPrefix = $"{channelVersion}.";
        string? best = null;
        foreach (var installed in _installedRuntimes)
        {
            if (installed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var ver = installed.Substring(prefix.Length);
                if (ver == channelVersion || ver.StartsWith(channelPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    if (best is null || CompareVersions(ver, best) > 0)
                        best = ver;
                }
            }
        }
        return best;
    }

    public static string? GetInstalledSdkVersion(string channelVersion)
    {
        if (_installedSdks is null) return null;
        var channelPrefix = $"{channelVersion}.";
        string? best = null;
        foreach (var installed in _installedSdks)
        {
            if (installed == channelVersion || installed.StartsWith(channelPrefix, StringComparison.OrdinalIgnoreCase))
            {
                if (best is null || CompareVersions(installed, best) > 0)
                    best = installed;
            }
        }
        return best;
    }

    public static string? GetInstalledFrameworkVersion(string key)
    {
        return _installedFrameworkVersions?.TryGetValue(key, out var ver) == true ? ver : null;
    }

    private static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            if (int.TryParse(pa[i], out var va) && int.TryParse(pb[i], out var vb))
            {
                if (va != vb) return va.CompareTo(vb);
            }
            else
            {
                var c = string.Compare(pa[i], pb[i], StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
            }
        }
        return pa.Length.CompareTo(pb.Length);
    }

    private static async Task FetchChannelsAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(ReleasesIndexUrl);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement.GetProperty("releases-index");

            var channels = new List<DotnetChannelInfo>();
            foreach (var ch in root.EnumerateArray())
            {
                channels.Add(new DotnetChannelInfo
                {
                    ChannelVersion = ch.GetProperty("channel-version").GetString() ?? "",
                    LatestRelease = ch.GetProperty("latest-release").GetString() ?? "",
                    LatestRuntime = ch.TryGetProperty("latest-runtime", out var lr) ? lr.GetString() ?? "" : "",
                    LatestSdk = ch.TryGetProperty("latest-sdk", out var ls) ? ls.GetString() ?? "" : "",
                    SupportPhase = ch.GetProperty("support-phase").GetString() ?? "",
                    ReleaseType = ch.TryGetProperty("release-type", out var rt) ? rt.GetString() ?? "" : "",
                    EolDate = ch.TryGetProperty("eol-date", out var eol) ? eol.GetString() : null,
                    ReleasesJsonUrl = ch.GetProperty("releases.json").GetString() ?? "",
                    Security = ch.TryGetProperty("security", out var sec) && sec.GetBoolean()
                });
            }

            _channels = channels
                .Where(c => !string.IsNullOrEmpty(c.ChannelVersion))
                .OrderByDescending(c =>
                {
                    var parts = c.ChannelVersion.Split('.');
                    return parts.Length > 0 && int.TryParse(parts[0], out var v) ? v : 0;
                })
                .ThenByDescending(c =>
                {
                    var parts = c.ChannelVersion.Split('.');
                    return parts.Length > 1 && int.TryParse(parts[1], out var v) ? v : 0;
                })
                .ToList();
        }
        catch
        {
            _channels = [];
        }
    }

    private static async Task BuildInstallablesAsync()
    {
        if (_channels is null) return;

        var items = new List<DotnetInstallableItem>();
        var rid = CurrentArch;

        AddDotnetFrameworkItems(items);

        foreach (var channel in _channels)
        {
            if (channel.ChannelVersion is "1.0" or "1.1" or "2.0" or "2.1" or "2.2" or "3.0")
                continue;

            if (string.IsNullOrEmpty(channel.ReleasesJsonUrl))
                continue;

            DotnetReleaseData? releaseData = null;
            try
            {
                var json = await _http.GetStringAsync(channel.ReleasesJsonUrl);
                releaseData = JsonSerializer.Deserialize<DotnetReleaseData>(json);
            }
            catch { continue; }

            if (releaseData?.Releases is null || releaseData.Releases.Count == 0)
                continue;

            var latestRelease = releaseData.Releases[0];

            if (latestRelease.Runtime?.Files is not null)
            {
                var f = latestRelease.Runtime.Files.FirstOrDefault(f => f.Rid == rid && f.Name?.EndsWith(".exe") == true);
                if (f is not null)
                {
                    var item = new DotnetInstallableItem
                    {
                        ComponentType = DotnetComponentType.Runtime,
                        Version = latestRelease.Runtime.Version ?? "",
                        DisplayName = $".NET Runtime {channel.ChannelVersion}",
                        Rid = rid, DownloadUrl = f.Url ?? "", FileName = f.Name ?? "",
                        ChannelVersion = channel.ChannelVersion
                    };
                    UpdateItemStatus(item);
                    items.Add(item);
                }
            }

            if (latestRelease.AspNetCoreRuntime?.Files is not null)
            {
                var f = latestRelease.AspNetCoreRuntime.Files.FirstOrDefault(f => f.Rid == rid && f.Name?.EndsWith(".exe") == true);
                if (f is not null)
                {
                    var item = new DotnetInstallableItem
                    {
                        ComponentType = DotnetComponentType.AspNetCoreRuntime,
                        Version = latestRelease.AspNetCoreRuntime.Version ?? "",
                        DisplayName = $"ASP.NET Core Runtime {channel.ChannelVersion}",
                        Rid = rid, DownloadUrl = f.Url ?? "", FileName = f.Name ?? "",
                        ChannelVersion = channel.ChannelVersion
                    };
                    UpdateItemStatus(item);
                    items.Add(item);
                }
            }

            if (latestRelease.WindowsDesktop?.Files is not null)
            {
                var f = latestRelease.WindowsDesktop.Files.FirstOrDefault(f => f.Rid == rid && f.Name?.EndsWith(".exe") == true);
                if (f is not null)
                {
                    var item = new DotnetInstallableItem
                    {
                        ComponentType = DotnetComponentType.WindowsDesktopRuntime,
                        Version = latestRelease.WindowsDesktop.Version ?? "",
                        DisplayName = $"Windows Desktop Runtime {channel.ChannelVersion}",
                        Rid = rid, DownloadUrl = f.Url ?? "", FileName = f.Name ?? "",
                        ChannelVersion = channel.ChannelVersion
                    };
                    UpdateItemStatus(item);
                    items.Add(item);
                }
            }

            if (latestRelease.Sdk?.Files is not null)
            {
                var f = latestRelease.Sdk.Files.FirstOrDefault(f => f.Rid == rid && f.Name?.EndsWith(".exe") == true);
                if (f is not null)
                {
                    var item = new DotnetInstallableItem
                    {
                        ComponentType = DotnetComponentType.Sdk,
                        Version = latestRelease.Sdk.Version ?? "",
                        DisplayName = $".NET SDK {channel.ChannelVersion}",
                        Rid = rid, DownloadUrl = f.Url ?? "", FileName = f.Name ?? "",
                        ChannelVersion = channel.ChannelVersion
                    };
                    UpdateItemStatus(item);
                    items.Add(item);
                }
            }
        }

        _installables = items;
    }

    private static void AddDotnetFrameworkItems(List<DotnetInstallableItem> items)
    {
        var frameworkEntries = new (string Version, string DisplayName, string DownloadUrl, string FileName, bool UseDism)[]
        {
            ("4.8", ".NET Framework 4.8",
                "https://download.microsoft.com/download/f/3/a/f3a6af84-da23-40a5-8d1c-49cc10c8e76f/NDP48-x86-x64-AllOS-ENU.exe",
                "NDP48-x86-x64-AllOS-ENU.exe", false),
        };

        foreach (var entry in frameworkEntries)
        {
            var item = new DotnetInstallableItem
            {
                ComponentType = DotnetComponentType.DotnetFramework,
                Version = entry.Version,
                DisplayName = entry.DisplayName,
                Rid = "",
                DownloadUrl = entry.UseDism ? "dism" : entry.DownloadUrl,
                FileName = entry.FileName,
                ChannelVersion = entry.Version.StartsWith("4.") ? "Framework 4.x" : $"Framework {entry.Version}",
                UseDism = entry.UseDism
            };

            var installedVer = entry.Version.StartsWith("4.")
                ? GetInstalledFrameworkVersion("4.x")
                : GetInstalledFrameworkVersion(entry.Version);

            if (installedVer is not null)
            {
                var cmp = CompareVersions(installedVer.TrimStart('v', 'V'), entry.Version);
                if (cmp >= 0)
                {
                    item.Status = DotnetInstallStatus.Installed;
                    item.InstalledVersion = installedVer;
                }
            }

            items.Add(item);
        }
    }

    private static void UpdateItemStatus(DotnetInstallableItem item)
    {
        if (item.ComponentType == DotnetComponentType.DotnetFramework)
        {
            var installedVer = item.Version.StartsWith("4.")
                ? GetInstalledFrameworkVersion("4.x")
                : GetInstalledFrameworkVersion(item.Version);
            item.Status = installedVer is not null ? DotnetInstallStatus.Installed : DotnetInstallStatus.NotInstalled;
            item.InstalledVersion = installedVer;
            return;
        }

        switch (item.ComponentType)
        {
            case DotnetComponentType.Runtime:
            case DotnetComponentType.AspNetCoreRuntime:
            case DotnetComponentType.WindowsDesktopRuntime:
                {
                    var runtimeName = item.ComponentType switch
                    {
                        DotnetComponentType.AspNetCoreRuntime => "Microsoft.AspNetCore.App",
                        DotnetComponentType.WindowsDesktopRuntime => "Microsoft.WindowsDesktop.App",
                        _ => "Microsoft.NETCore.App"
                    };
                    var installedVer = GetInstalledRuntimeVersion(runtimeName, item.ChannelVersion);
                    item.Status = installedVer is not null ? DotnetInstallStatus.Installed : DotnetInstallStatus.NotInstalled;
                    item.InstalledVersion = installedVer;
                }
                break;
            case DotnetComponentType.Sdk:
                {
                    var installedVer = GetInstalledSdkVersion(item.ChannelVersion);
                    item.Status = installedVer is not null ? DotnetInstallStatus.Installed : DotnetInstallStatus.NotInstalled;
                    item.InstalledVersion = installedVer;
                }
                break;
        }
    }

    public static void EnqueueDownloadAndInstall(DotnetInstallableItem item)
    {
        if (item.Status == DotnetInstallStatus.Installed) return;

        item.Status = DotnetInstallStatus.Downloading;
        DataChanged?.Invoke();

        Directory.CreateDirectory(_downloadDir);

        if (item.UseDism)
        {
            EnqueueDismInstall(item);
            return;
        }

        IDownloadPostProcessor? postProcessor = null;
        if (item.FileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            postProcessor = new DelegatePostProcessor("安装 .NET 组件", async (downloadedFile, dest, statusProgress, ct) =>
            {
                statusProgress?.Report("正在启动安装程序（需 UAC 确认）...");
                await Task.Run(() =>
                {
                    var psi = new ProcessStartInfo
                    {
                        FileName = downloadedFile,
                        Arguments = item.ComponentType == DotnetComponentType.DotnetFramework
                            ? "/q /norestart"
                            : "/install /quiet /norestart",
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    using var proc = Process.Start(psi);
                    proc?.WaitForExit();
                }, ct);

                await RefreshInstalledAsync();
                DataChanged?.Invoke();
            });
        }

        DownloadQueueService.Enqueue(
            displayName: item.DisplayName,
            downloadUrl: item.DownloadUrl,
            destinationPath: _downloadDir,
            postProcessor: postProcessor,
            description: $"{GetComponentTypeLabel(item.ComponentType)} {item.Version}",
            glyph: GetComponentTypeGlyph(item.ComponentType),
            tag: item);
    }

    private static void EnqueueDismInstall(DotnetInstallableItem item)
    {
        var postProcessor = new DelegatePostProcessor("启用 .NET Framework 3.5", async (downloadedFile, dest, statusProgress, ct) =>
        {
            statusProgress?.Report("正在通过 DISM 启用 .NET Framework 3.5（需 UAC 确认）...");
            await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "dism.exe",
                    Arguments = "/online /enable-feature /featurename:NetFx3 /All /LimitAccess",
                    UseShellExecute = true,
                    Verb = "runas"
                };
                using var proc = Process.Start(psi);
                proc?.WaitForExit();
            }, ct);

            await RefreshInstalledAsync();
            DataChanged?.Invoke();
        });

        DownloadQueueService.EnqueueWithResolver(
            displayName: item.DisplayName,
            urlResolver: _ => Task.FromResult(new ResolvedDownloadUrl("dism://netfx3", "dism-netfx3.placeholder")),
            destinationPath: _downloadDir,
            postProcessor: postProcessor,
            description: "DISM 启用 Windows 功能",
            glyph: GetComponentTypeGlyph(item.ComponentType),
            tag: item);
    }

    public static void EnqueueDownloadOnly(DotnetInstallableItem item)
    {
        if (item.Status == DotnetInstallStatus.Installed) return;

        item.Status = DotnetInstallStatus.Downloading;
        DataChanged?.Invoke();

        Directory.CreateDirectory(_downloadDir);

        if (item.UseDism)
        {
            OpenDownloadPage(item);
            item.Status = DotnetInstallStatus.NotInstalled;
            DataChanged?.Invoke();
            return;
        }

        DownloadQueueService.Enqueue(
            displayName: item.DisplayName,
            downloadUrl: item.DownloadUrl,
            destinationPath: _downloadDir,
            description: $"{GetComponentTypeLabel(item.ComponentType)} {item.Version}",
            glyph: GetComponentTypeGlyph(item.ComponentType),
            tag: item);
    }

    public static void OpenDownloadPage(DotnetInstallableItem item)
    {
        var url = item.ComponentType switch
        {
            DotnetComponentType.DotnetFramework when item.UseDism => null,
            DotnetComponentType.DotnetFramework => item.DownloadUrl,
            _ => item.DownloadUrl
        };

        if (url is null)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "dism.exe",
                    Arguments = "/online /enable-feature /featurename:NetFx3 /All",
                    UseShellExecute = true,
                    Verb = "runas"
                });
            }
            catch { }
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch { }
    }

    public static string GetSupportPhaseLabel(string phase) => phase switch
    {
        "active" => "活跃",
        "maintenance" => "维护",
        "lts" => "LTS",
        "preview" => "预览",
        "go-live" => "Go-Live",
        "eol" => "已过期",
        _ => phase
    };

    public static Color GetSupportPhaseColor(string phase) => phase switch
    {
        "active" => ThemeColors.AccentGreen,
        "maintenance" => ThemeColors.AccentOrange,
        "lts" => ThemeColors.AccentBlue,
        "preview" => ThemeColors.AccentPurple,
        "go-live" => ThemeColors.AccentBlue,
        "eol" => ThemeColors.DimText,
        _ => ThemeColors.DimText
    };

    public static string GetComponentTypeLabel(DotnetComponentType type) => type switch
    {
        DotnetComponentType.Runtime => "Runtime",
        DotnetComponentType.Sdk => "SDK",
        DotnetComponentType.AspNetCoreRuntime => "ASP.NET Core",
        DotnetComponentType.WindowsDesktopRuntime => "Desktop",
        DotnetComponentType.DotnetFramework => "Framework",
        _ => type.ToString()
    };

    public static string GetComponentTypeGlyph(DotnetComponentType type) => type switch
    {
        DotnetComponentType.Runtime => "\uE950",
        DotnetComponentType.Sdk => "\uE943",
        DotnetComponentType.AspNetCoreRuntime => "\uEB41",
        DotnetComponentType.WindowsDesktopRuntime => "\uE8F1",
        DotnetComponentType.DotnetFramework => "\uE8F1",
        _ => "\uE950"
    };
}

internal sealed class DotnetReleaseData
{
    [JsonPropertyName("releases")]
    public List<DotnetRelease>? Releases { get; set; }
}

internal sealed class DotnetRelease
{
    [JsonPropertyName("runtime")]
    public DotnetComponentData? Runtime { get; set; }
    [JsonPropertyName("sdk")]
    public DotnetComponentData? Sdk { get; set; }
    [JsonPropertyName("aspnetcore-runtime")]
    public DotnetComponentData? AspNetCoreRuntime { get; set; }
    [JsonPropertyName("windowsdesktop")]
    public DotnetComponentData? WindowsDesktop { get; set; }
}

internal sealed class DotnetComponentData
{
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    [JsonPropertyName("files")]
    public List<DotnetFile>? Files { get; set; }
}

internal sealed class DotnetFile
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("rid")]
    public string? Rid { get; set; }
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}
