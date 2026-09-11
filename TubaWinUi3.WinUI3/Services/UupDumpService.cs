using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services;

public sealed class UupBuildInfo
{
    public string UpdateId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Build { get; set; } = "";
    public string Architecture { get; set; } = "";
    public DateTime? DateAdded { get; set; }
    public string Channel { get; set; } = "";

    public string DateDisplay => DateAdded?.ToLocalTime().ToString("yyyy-MM-dd") ?? "";

    public string DetailsDisplay
    {
        get
        {
            var parts = new[] { Build, Architecture, Channel, DateDisplay }.Where(s => !string.IsNullOrEmpty(s));
            return string.Join(" · ", parts);
        }
    }
}

public sealed class UupLanguageInfo
{
    public string Code { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class UupEditionInfo
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class UupFileEntry
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public long Size { get; set; }
    public string Sha1 { get; set; } = "";
}

public sealed class UupFileSetInfo
{
    public string UpdateId { get; set; } = "";
    public string Language { get; set; } = "";
    public string Edition { get; set; } = "";
    public string UpdateName { get; set; } = "";
    public string Architecture { get; set; } = "";
    public string Build { get; set; } = "";
    public List<UupFileEntry> Files { get; set; } = [];

    public long TotalSize => Files.Sum(f => f.Size);
}

/// <summary>UUP → ISO 转换选项（与 uupdump.net「Summary」页的 Conversion options 一致）。</summary>
public sealed class UupConvertOptions
{
    /// <summary>集成更新（AddUpdates，官网默认勾选）。</summary>
    public bool AddUpdates { get; set; } = true;

    /// <summary>运行组件清理（Cleanup，减小体积但显著变慢）。</summary>
    public bool Cleanup { get; set; }

    /// <summary>集成 .NET Framework 3.5（NetFx3）。</summary>
    public bool NetFx3 { get; set; }

    /// <summary>使用 solid (ESD) 压缩（wim2esd + vwim2esd）。</summary>
    public bool Wim2Esd { get; set; }

    /// <summary>跳过商店应用完整版集成（SkipApps，默认跳过以加快转换）。</summary>
    public bool SkipApps { get; set; } = true;

    /// <summary>附加版本（虚拟版本）列表，使用转换器命名（如 Enterprise / CoreSingleLanguage）。</summary>
    public List<string> VirtualEditions { get; set; } = [];

    public bool HasVirtualEditions => VirtualEditions.Count > 0;
}

/// <summary>附加版本定义：转换器内部名 + 中文显示名。</summary>
public sealed record UupVirtualEditionInfo(string Name, string DisplayName);

/// <summary>官方转换工具清单条目：下载地址、本地文件名（清单 out=）、SHA-256。</summary>
internal sealed record UupConverterFileInfo(string Url, string FileName, string Sha256);

/// <summary>UUP dump JSON API 错误，ShortCode 为 API 返回的错误码（如 USER_RATE_LIMITED）。</summary>
public sealed class UupDumpApiException : Exception
{
    public string ShortCode { get; }

    public UupDumpApiException(string shortCode, string userMessage) : base(userMessage)
    {
        ShortCode = shortCode;
    }
}

/// <summary>
/// UUP dump 官方 JSON API（https://git.uupdump.net/uup-dump/json-api，部署于 api.uupdump.net）
/// 的封装。所有数据（已知构建 / 语言 / 版本 / 文件直链）均来自 JSON API，
/// 文件直链指向微软官方 CDN（*.delivery.mp.microsoft.com）。
/// 注意：文件直链有效期仅约 15 分钟，长任务需重新调用 GetFilesAsync 刷新（见 DownloadQueueService 多文件自动重试）。
/// </summary>
public static class UupDumpService
{
    private const string ApiHost = "api.uupdump.net";
    private const string ApiBase = "https://api.uupdump.net";

    // 转换器与解压器来自 UUP dump 官方 misc 仓库，与官网生成的下载包一致。
    // 下载地址与 SHA-256 运行时从官方清单 autodl_files/converter_windows 获取
    // （该清单即官网打包用的源文件，转换器升级时自动跟随），拉取失败时用下面的兜底值。
    private const string ConverterManifestUrl =
        "https://git.uupdump.net/uup-dump/misc/raw/branch/master/autodl_files/converter_windows";
    private const string ConverterMirrorBase = "https://git.uupdump.net/uup-dump/misc/raw/branch/master/";
    private const string SevenZipFileName = "7zr.exe";
    private const string ConverterArchiveGlob = "uup-converter-wimlib*.7z";
    private static readonly UupConverterFileInfo[] FallbackConverterFiles =
    [
        new("https://uupdump.net/misc/7zr.exe", SevenZipFileName,
            "72c98287b2e8f85ea7bb87834b6ce1ce7ce7f41a8c97a81b307d4d4bf900922b"),
        new("https://uupdump.net/misc/uup-converter-wimlib-v125r.7z", "uup-converter-wimlib.7z",
            "bc2e7a45c6e8d3304da487d21d4e33c21d20cc39ae4725b4a378683e18356165"),
    ];

// IPv4 优先连接：见 HttpClientFactory（国内到国际 CDN 的 IPv6 路径常被静默丢弃）
    private static readonly HttpClient _http = HttpClientFactory.CreateIpv4Preferred(TimeSpan.FromSeconds(60));

    private static readonly Dictionary<string, string> LanguageNames = new()
    {
        ["ar-sa"] = "阿拉伯语", ["bg-bg"] = "保加利亚语", ["zh-cn"] = "简体中文",
        ["zh-tw"] = "繁体中文", ["hr-hr"] = "克罗地亚语", ["cs-cz"] = "捷克语",
        ["da-dk"] = "丹麦语", ["nl-nl"] = "荷兰语", ["en-gb"] = "英语(英)",
        ["en-us"] = "英语(美)", ["et-ee"] = "爱沙尼亚语", ["fi-fi"] = "芬兰语",
        ["fr-ca"] = "法语(加)", ["fr-fr"] = "法语", ["de-de"] = "德语",
        ["el-gr"] = "希腊语", ["he-il"] = "希伯来语", ["hu-hu"] = "匈牙利语",
        ["it-it"] = "意大利语", ["ja-jp"] = "日语", ["ko-kr"] = "韩语",
        ["lv-lv"] = "拉脱维亚语", ["lt-lt"] = "立陶宛语", ["nb-no"] = "挪威语",
        ["pl-pl"] = "波兰语", ["pt-br"] = "葡萄牙语(巴)", ["pt-pt"] = "葡萄牙语",
        ["ro-ro"] = "罗马尼亚语", ["ru-ru"] = "俄语", ["sr-latn-rs"] = "塞尔维亚语",
        ["sk-sk"] = "斯洛伐克语", ["sl-si"] = "斯洛文尼亚语", ["es-mx"] = "西班牙语(墨)",
        ["es-es"] = "西班牙语", ["sv-se"] = "瑞典语", ["th-th"] = "泰语",
        ["tr-tr"] = "土耳其语", ["uk-ua"] = "乌克兰语", ["neutral"] = "任意语言"
    };

    private static readonly Dictionary<string, string> EditionNames = new()
    {
        ["CORE"] = "Windows 家庭版",
        ["COREN"] = "Windows 家庭版 N",
        ["CORESINGLELANGUAGE"] = "Windows 家庭单语言版",
        ["CORECOUNTRYSPECIFIC"] = "Windows 家庭中文版",
        ["PROFESSIONAL"] = "Windows 专业版",
        ["PROFESSIONALN"] = "Windows 专业版 N",
        ["PROFESSIONALWORKSTATION"] = "Windows 专业工作站版",
        ["PROFESSIONALWORKSTATIONN"] = "Windows 专业工作站版 N",
        ["PROFESSIONALEDUCATION"] = "Windows 专业教育版",
        ["PROFESSIONALEDUCATIONN"] = "Windows 专业教育版 N",
        ["EDUCATION"] = "Windows 教育版",
        ["EDUCATIONN"] = "Windows 教育版 N",
        ["ENTERPRISE"] = "Windows 企业版",
        ["ENTERPRISEN"] = "Windows 企业版 N",
        ["ENTERPRISEG"] = "Windows 企业版 G",
        ["ENTERPRISEGN"] = "Windows 企业版 G N",
        ["ENTERPRISES"] = "Windows 企业版 S",
        ["ENTERPRISESN"] = "Windows 企业版 S N",
        ["SERVERRDSH"] = "Windows 企业多会话版",
        ["IOTENTERPRISE"] = "Windows IoT 企业版",
        ["IOTENTERPRISEK"] = "Windows IoT 企业版订阅",
        ["IOTENTERPRISES"] = "Windows IoT 企业版 S",
        ["IOTENTERPRISESK"] = "Windows IoT 企业版 S 订阅",
        ["CLOUD"] = "Windows Cloud",
        ["CLOUDN"] = "Windows Cloud N",
        ["CLOUDE"] = "Windows Cloud Edition",
        ["CLOUDEN"] = "Windows Cloud Edition N",
        ["SERVERSTANDARD"] = "Windows Server 标准版",
        ["SERVERSTANDARDCORE"] = "Windows Server 标准版 (Core)",
        ["SERVERDATACENTER"] = "Windows Server 数据中心版",
        ["SERVERDATACENTERCORE"] = "Windows Server 数据中心版 (Core)",
        ["SERVERTURBINE"] = "Windows Server Turbine",
        ["SERVERTURBINECORE"] = "Windows Server Turbine (Core)",
        ["SERVERAZURESTACKHCICOR"] = "Windows Server Azure Stack HCI (Core)",
        ["PPIPRO"] = "Windows Team",
        ["STARTER"] = "Windows 入门版",
        ["STARTERN"] = "Windows 入门版 N"
    };

    /// <summary>版本选择列表中的推荐顺序（越靠前越优先展示）。</summary>
    private static readonly string[] EditionPreferenceOrder =
    [
        "PROFESSIONAL", "CORE", "CORECOUNTRYSPECIFIC", "CORESINGLELANGUAGE",
        "ENTERPRISE", "EDUCATION", "PROFESSIONALWORKSTATION", "IOTENTERPRISE",
        "SERVERRDSH", "SERVERSTANDARD", "SERVERDATACENTER",
    ];

    public static IReadOnlyDictionary<string, string> GetLanguageNames() => LanguageNames;
    public static IReadOnlyDictionary<string, string> GetEditionNames() => EditionNames;

    public static string GetEditionDisplayName(string editionId) =>
        EditionNames.TryGetValue(editionId, out var name) ? name : editionId;

    public static string GetLanguageDisplayName(string code) =>
        LanguageNames.TryGetValue(code, out var name) ? name : code;

    // ==================== JSON API 调用 ====================

    /// <summary>获取已知构建列表（对应官网「浏览已知构建」，无接口限流）。search 支持版本号/关键词。</summary>
    public static async Task<List<UupBuildInfo>> GetKnownBuildsAsync(string? search = null, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/listid.php?sortByDate=1";
        if (!string.IsNullOrWhiteSpace(search))
            url += $"&search={Uri.EscapeDataString(search.Trim())}";

        using var doc = await GetJsonAsync(url, ct);
        return ParseBuildsJson(doc.RootElement.GetProperty("response"));
    }

    /// <summary>获取某构建可选的语言列表。</summary>
    public static async Task<List<UupLanguageInfo>> GetLanguagesAsync(string updateId, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/listlangs.php?id={Uri.EscapeDataString(updateId)}";
        using var doc = await GetJsonAsync(url, ct);
        return ParseLanguagesJson(doc.RootElement.GetProperty("response"));
    }

    /// <summary>获取某构建在指定语言下的可用版本列表。</summary>
    public static async Task<List<UupEditionInfo>> GetEditionsAsync(string updateId, string lang, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/listeditions.php?id={Uri.EscapeDataString(updateId)}&lang={Uri.EscapeDataString(lang)}";
        using var doc = await GetJsonAsync(url, ct);
        return ParseEditionsJson(doc.RootElement.GetProperty("response"));
    }

    /// <summary>
    /// 获取某构建指定语言+版本的文件清单。
    /// withLinks=false 时走 API 的 noLinks 快速通道（不触发限流），但不含下载直链。
    /// </summary>
    public static async Task<UupFileSetInfo> GetFilesAsync(string updateId, string lang, string edition, bool withLinks = true, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/get.php?id={Uri.EscapeDataString(updateId)}&lang={Uri.EscapeDataString(lang)}&edition={Uri.EscapeDataString(edition)}&noLinks={(withLinks ? 0 : 1)}";
        using var doc = await GetJsonAsync(url, ct);
        var set = ParseFilesJson(doc.RootElement.GetProperty("response"));
        set.UpdateId = updateId;
        set.Language = lang;
        set.Edition = edition;
        return set;
    }

    /// <summary>检测当前系统适合的 UUP 架构标识（amd64 / arm64 / x86）。</summary>
    public static string GetSuggestedArch()
    {
        return RuntimeInformation.OSArchitecture switch
        {
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "amd64",
        };
    }

    /// <summary>
    /// UUP 包根目录（每个下载占一个包子目录，内含 UUPs 文件集与转换产物）。
    /// 用户未自定义「WindowsImageDownloadDir」时为 下载\UUPDump。
    /// </summary>
    public static string GetDownloadDir()
    {
        var custom = AppSettings.Get("WindowsImageDownloadDir");
        var root = string.IsNullOrWhiteSpace(custom)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads")
            : custom.Trim();
        var dir = Path.Combine(root, "UUPDump");
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>某次 UUP 下载的包目录（UUPs/ 子目录存放文件集，转换脚本与 ISO 输出在包根目录）。同名目录复用，便于断点续传。</summary>
    public static (string PackageDir, string UupsDir) GetPackageDirs(string build, string lang, string edition)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var name = new StringBuilder($"{build}_{lang}_{edition}").Replace(' ', '_');
        for (var i = 0; i < name.Length; i++)
        {
            if (invalid.Contains(name[i])) name[i] = '_';
        }

        var packageDir = Path.Combine(GetDownloadDir(), name.ToString());
        return (packageDir, Path.Combine(packageDir, "UUPs"));
    }

    /// <summary>
    /// 可从指定基础版本合成的附加版本列表（对照 uupdump.net「Summary」页选项与官网 Required edition 映射）。
    /// </summary>
    public static List<UupVirtualEditionInfo> GetVirtualEditionsForBase(string baseEditionId)
    {
        return baseEditionId.ToUpperInvariant() switch
        {
            "PROFESSIONAL" =>
            [
                new UupVirtualEditionInfo("ProfessionalWorkstation", "专业工作站版"),
                new UupVirtualEditionInfo("ProfessionalEducation", "专业教育版"),
                new UupVirtualEditionInfo("Education", "教育版"),
                new UupVirtualEditionInfo("Enterprise", "企业版"),
                new UupVirtualEditionInfo("ServerRdsh", "企业多会话版"),
                new UupVirtualEditionInfo("IoTEnterprise", "IoT 企业版"),
                new UupVirtualEditionInfo("IoTEnterpriseK", "IoT 企业版订阅"),
            ],
            "PROFESSIONALN" =>
            [
                new UupVirtualEditionInfo("ProfessionalWorkstationN", "专业工作站版 N"),
                new UupVirtualEditionInfo("ProfessionalEducationN", "专业教育版 N"),
                new UupVirtualEditionInfo("EducationN", "教育版 N"),
                new UupVirtualEditionInfo("EnterpriseN", "企业版 N"),
            ],
            "CORE" =>
            [
                new UupVirtualEditionInfo("CoreSingleLanguage", "家庭单语言版"),
            ],
            _ => [],
        };
    }

    // ==================== 下载管线（文件集 + ISO 转换） ====================

    /// <summary>构造多文件下载解析器：每次调用都重新请求文件列表，拿到新鲜的微软 CDN 直链。</summary>
    public static Func<CancellationToken, Task<List<ResolvedDownloadUrl>>> CreateMultiFileResolver(string updateId, string lang, string edition)
    {
        return async ct =>
        {
            var set = await GetFilesAsync(updateId, lang, edition, withLinks: true, ct);
            if (set.Files.Count == 0)
                throw new UupDumpApiException("NO_FILES", "该版本没有可下载的文件，请尝试其他版本。");

            return set.Files
                .Select(f => new ResolvedDownloadUrl(f.Url, f.Name, f.Size))
                .ToList();
        };
    }

    /// <summary>构造「下载完成 → 自动转换为 ISO」的后处理器。destDir 为 UUPs 目录，包根目录是其上级。</summary>
    public static IDownloadPostProcessor CreateIsoPostProcessor(string title)
    {
        return new DelegatePostProcessor("UUP 转 ISO", async (downloadedPath, destDir, progress, ct) =>
        {
            var root = Path.GetDirectoryName(destDir.TrimEnd(Path.DirectorySeparatorChar))
                ?? throw new InvalidOperationException("无法确定转换目录。");
            Directory.CreateDirectory(root);

            progress?.Report("正在准备官方转换工具...");
            var filesDir = Path.Combine(root, "files");
            Directory.CreateDirectory(filesDir);
            await EnsureConverterFilesAsync(filesDir, progress, ct);

            // ConvertConfig.ini 在加入下载队列时已生成（下载期间用户可自行编辑，此处不覆盖）

            progress?.Report("正在解压转换工具...");
            await ExtractConverterAsync(root, ct);

            var convertCmd = Path.Combine(root, "convert-UUP.cmd");
            if (!File.Exists(convertCmd))
                throw new InvalidOperationException("转换工具解压后缺少 convert-UUP.cmd，请重试。");

            progress?.Report("正在启动转换脚本（ISO 生成约需 10~30 分钟，可在转换窗口查看进度）...");
            App.MainWindow?.DispatcherQueue.TryEnqueue(() =>
            {
                ScriptRunnerWindow.ShowAndRun(
                    "cmd.exe /c convert-UUP.cmd",
                    workingDir: root,
                    title: $"UUP 转 ISO - {title}");
            });

            // 转换脚本在独立窗口中运行，完成时 ISO 输出在 root 下
            await Task.CompletedTask;
        });
    }

    /// <summary>
    /// 在包根目录生成 ConvertConfig.ini（与 uupdump.net 生成的官方包格式一致）。
    /// 在加入下载队列时调用；下载期间用户可手动编辑，转换脚本启动时以该文件为准。
    /// </summary>
    public static void WriteConvertConfigIni(string packageDir, UupConvertOptions options)
    {
        Directory.CreateDirectory(packageDir);
        File.WriteAllText(Path.Combine(packageDir, "ConvertConfig.ini"), BuildConvertConfigIni(options));
    }

    internal static string BuildConvertConfigIni(UupConvertOptions? options = null)
    {
        options ??= new UupConvertOptions();

        string Flag(bool v) => v ? "1" : "0";

        var sb = new StringBuilder();
        sb.AppendLine("[convert-UUP]");
        sb.AppendLine("AutoStart    =1");
        sb.AppendLine($"AddUpdates   ={Flag(options.AddUpdates)}");
        sb.AppendLine($"Cleanup      ={Flag(options.Cleanup)}");
        sb.AppendLine("ResetBase    =0");
        sb.AppendLine($"NetFx3       ={Flag(options.NetFx3)}");
        sb.AppendLine($"StartVirtual ={Flag(options.HasVirtualEditions)}");
        sb.AppendLine($"wim2esd      ={Flag(options.Wim2Esd)}");
        sb.AppendLine("wim2swm      =0");
        sb.AppendLine("SkipISO      =0");
        sb.AppendLine("SkipWinRE    =0");
        sb.AppendLine("LCUwinre     =0");
        sb.AppendLine("LCUmsuExpand =0");
        sb.AppendLine("UpdtBootFiles=0");
        sb.AppendLine("ForceDism    =0");
        sb.AppendLine("RefESD       =0");
        sb.AppendLine("SkipLCUmsu   =0");
        sb.AppendLine("SkipEdge     =0");
        sb.AppendLine("AutoExit     =1");
        sb.AppendLine("DisableUpdatingUpgrade=0");
        sb.AppendLine("AddDrivers   =0");
        sb.AppendLine("Drv_Source   =\\Drivers");
        sb.AppendLine();
        sb.AppendLine("[Store_Apps]");
        sb.AppendLine($"SkipApps     ={Flag(options.SkipApps)}");
        sb.AppendLine("AppsLevel    =0");
        sb.AppendLine("StubAppsFull =0");
        sb.AppendLine("CustomList   =0");
        sb.AppendLine();
        sb.AppendLine("[create_virtual_editions]");
        sb.AppendLine("vUseDism     =1");
        sb.AppendLine("vAutoStart   =1");
        sb.AppendLine("vDeleteSource=0");
        sb.AppendLine("vPreserve    =0");
        sb.AppendLine($"vwim2esd     ={Flag(options.Wim2Esd)}");
        sb.AppendLine("vwim2swm     =0");
        sb.AppendLine("vSkipISO     =0");
        sb.AppendLine($"vAutoEditions={string.Join(",", options.VirtualEditions)}");
        sb.AppendLine("vSortEditions=");
        return sb.ToString();
    }

    /// <summary>获取官方转换工具清单（aria2 格式）；拉取失败或解析为空时返回内置兜底列表。</summary>
    internal static async Task<List<UupConverterFileInfo>> GetConverterFileListAsync(CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ConverterManifestUrl);
            request.Headers.UserAgent.ParseAdd("TubaWinUi3-UupDump/2.0");
            using var response = await _http.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                var text = await response.Content.ReadAsStringAsync(ct);
                var parsed = ParseConverterManifest(text);
                if (parsed.Count > 0) return parsed;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            // 清单拉取失败不影响流程，退回内置兜底列表
        }

        return FallbackConverterFiles.ToList();
    }

    /// <summary>解析官方 converter_windows 清单（aria2 输入格式：url 行 + out= + checksum=sha-256=）。</summary>
    internal static List<UupConverterFileInfo> ParseConverterManifest(string text)
    {
        var result = new List<UupConverterFileInfo>();
        string? url = null, outFile = null, hash = null;

        void Flush()
        {
            if (url is not null && outFile is not null && hash is not null)
                result.Add(new UupConverterFileInfo(url, outFile, hash));
            url = outFile = hash = null;
        }

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            if (url is null)
            {
                if (line.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    url = line;
                continue;
            }

            if (line.StartsWith("out=", StringComparison.Ordinal))
                outFile = line["out=".Length..].Trim();
            else if (line.StartsWith("checksum=sha-256=", StringComparison.OrdinalIgnoreCase))
                hash = line["checksum=sha-256=".Length..].Trim();
        }
        Flush();

        return result;
    }

    /// <summary>下载并校验转换工具文件（SHA-256 校验，官方地址 + git.uupdump.net raw 镜像双源回退，已存在且校验通过则跳过）。</summary>
    private static async Task EnsureConverterFilesAsync(string filesDir, IProgress<string>? progress, CancellationToken ct)
    {
        var files = await GetConverterFileListAsync(ct);

        foreach (var file in files)
        {
            var destPath = Path.Combine(filesDir, file.FileName);
            var remoteName = Path.GetFileName(new Uri(file.Url).LocalPath);

            if (File.Exists(destPath) && await VerifySha256Async(destPath, file.Sha256, ct))
                continue;

            // 远端文件名带版本号（如 uup-converter-wimlib-v125r.7z），
            // 镜像地址按远端名拼接；本地保存名用清单的 out=
            var sources = new[] { file.Url, ConverterMirrorBase + remoteName };

            Exception? lastError = null;
            var ok = false;
            foreach (var source in sources)
            {
                for (var attempt = 0; attempt < 2 && !ok; attempt++)
                {
                    try
                    {
                        ct.ThrowIfCancellationRequested();
                        progress?.Report($"正在下载转换工具 {file.FileName}...");
                        await DownloadToFileAsync(source, destPath, ct);
                        if (await VerifySha256Async(destPath, file.Sha256, ct))
                        {
                            ok = true;
                            break;
                        }
                        throw new InvalidDataException($"{file.FileName} SHA-256 校验不通过");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        try { File.Delete(destPath); } catch { }
                    }
                }
                if (ok) break;
            }

            if (!ok)
                throw new InvalidOperationException($"转换工具 {file.FileName} 下载失败：{lastError?.Message}", lastError);
        }
    }

    private static async Task DownloadToFileAsync(string url, string destPath, CancellationToken ct)
    {
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fs = File.Create(destPath);
        await stream.CopyToAsync(fs, ct);
    }

    internal static async Task<bool> VerifySha256Async(string filePath, string expectedSha256, CancellationToken ct)
    {
        try
        {
            await using var fs = File.OpenRead(filePath);
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hash = await sha.ComputeHashAsync(fs, ct);
            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash) sb.Append(b.ToString("x2"));
            return sb.ToString().Equals(expectedSha256, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static async Task ExtractConverterAsync(string root, CancellationToken ct)
    {
        var filesDir = Path.Combine(root, "files");
        var sevenZip = Path.Combine(filesDir, SevenZipFileName);
        if (!File.Exists(sevenZip))
            throw new InvalidOperationException("转换工具文件缺失，请重试。");

        var archive = Directory.GetFiles(filesDir, ConverterArchiveGlob).FirstOrDefault()
            ?? throw new InvalidOperationException("未找到转换工具压缩包，请重试。");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = sevenZip,
            Arguments = $"x \"{archive}\" -x!ConvertConfig.ini -y -o\"{root}\"",
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动解压工具。");
        await process.WaitForExitAsync(ct);
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"转换工具解压失败（退出码 {process.ExitCode}）。");
    }

    // ==================== JSON 解析（internal，供单元测试） ====================

    internal static List<UupBuildInfo> ParseBuildsJson(JsonElement response)
    {
        var builds = new List<UupBuildInfo>();
        if (!response.TryGetProperty("builds", out var buildsEl))
            return builds;

        // API 无 search 时返回数组；带 search 时 PHP 会输出以索引为键的对象，两种形态都兼容
        IEnumerable<JsonElement> items = buildsEl.ValueKind == JsonValueKind.Array
            ? buildsEl.EnumerateArray().ToList()
            : buildsEl.EnumerateObject().Select(p => p.Value).ToList();

        foreach (var v in items)
        {
            if (v.ValueKind != JsonValueKind.Object) continue;

            var title = GetString(v, "title");
            var uuid = GetString(v, "uuid");
            if (string.IsNullOrEmpty(title) || string.IsNullOrEmpty(uuid)) continue;

            var created = GetInt64(v, "created");
            builds.Add(new UupBuildInfo
            {
                UpdateId = uuid,
                Title = title,
                Build = GetString(v, "build"),
                Architecture = GetString(v, "arch"),
                DateAdded = created > 0 ? DateTimeOffset.FromUnixTimeSeconds(created).UtcDateTime : null,
                Channel = DeriveChannel(title),
            });
        }

        return builds;
    }

    internal static List<UupLanguageInfo> ParseLanguagesJson(JsonElement response)
    {
        var result = new List<UupLanguageInfo>();

        var fancy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (response.TryGetProperty("langFancyNames", out var fancyEl) && fancyEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in fancyEl.EnumerateObject())
                fancy[p.Name] = p.Value.GetString() ?? "";
        }

        if (response.TryGetProperty("langList", out var listEl) && listEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in listEl.EnumerateArray())
            {
                var code = v.GetString();
                if (string.IsNullOrEmpty(code)) continue;
                result.Add(new UupLanguageInfo
                {
                    Code = code,
                    DisplayName = BuildLanguageDisplay(code, fancy),
                });
            }
        }

        return result
            .OrderBy(l => l.Code == "zh-cn" ? 0 : l.Code.StartsWith("zh") ? 1 : 2)
            .ThenBy(l => l.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static List<UupEditionInfo> ParseEditionsJson(JsonElement response)
    {
        var result = new List<UupEditionInfo>();

        var fancy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (response.TryGetProperty("editionFancyNames", out var fancyEl) && fancyEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in fancyEl.EnumerateObject())
                fancy[p.Name] = p.Value.GetString() ?? "";
        }

        if (response.TryGetProperty("editionList", out var listEl) && listEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in listEl.EnumerateArray())
            {
                var id = v.GetString();
                if (string.IsNullOrEmpty(id)) continue;
                var display = EditionNames.TryGetValue(id, out var zh) ? zh
                    : fancy.TryGetValue(id, out var en) && !string.IsNullOrEmpty(en) ? en
                    : id;
                result.Add(new UupEditionInfo { Id = id, DisplayName = display });
            }
        }

        return result
            .OrderBy(e =>
            {
                var idx = Array.FindIndex(EditionPreferenceOrder, x => x.Equals(e.Id, StringComparison.OrdinalIgnoreCase));
                return idx >= 0 ? idx : EditionPreferenceOrder.Length;
            })
            .ToList();
    }

    internal static UupFileSetInfo ParseFilesJson(JsonElement response)
    {
        var set = new UupFileSetInfo
        {
            UpdateName = GetString(response, "updateName"),
            Architecture = GetString(response, "arch"),
            Build = GetString(response, "build"),
        };

        if (response.TryGetProperty("files", out var filesEl) && filesEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in filesEl.EnumerateObject())
            {
                var v = p.Value;
                var entry = new UupFileEntry
                {
                    Name = p.Name,
                    Url = GetString(v, "url"),
                    Size = GetInt64(v, "size"),
                    Sha1 = GetString(v, "sha1"),
                };
                // noLinks=1 的响应没有直链；无直链且大小为 0 的条目（异常数据）跳过
                if (entry.Size <= 0 && string.IsNullOrEmpty(entry.Url)) continue;
                set.Files.Add(entry);
            }
        }

        set.Files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return set;
    }

    // ==================== 内部辅助 ====================

    private static bool IsTransientError(UupDumpApiException ex)
    {
        // 网络抖动、超时、服务端 5xx 与限流可重试；业务错误（如 NO_UPDATE_FOUND）重试无意义
        return ex.ShortCode is "NETWORK" or "TIMEOUT" or "HTTP_429"
            || (ex.ShortCode.StartsWith("HTTP_5", StringComparison.Ordinal));
    }

    private static async Task<JsonDocument> GetJsonAsync(string url, CancellationToken ct)
    {
        // 网络层自动重试：IPv6 被丢弃/服务端抖动/限流时首次请求可能失败，
        // 二次请求直接走已恢复的连接；限流按官方文档为同资源 1 秒、切资源 10 秒窗口，多等一会儿再试
        UupDumpApiException? lastError = null;
        const int maxAttempts = 3;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            try
            {
                return await GetJsonOnceAsync(url, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (UupDumpApiException ex) when (IsTransientError(ex) && attempt < maxAttempts - 1)
            {
                lastError = ex;
                var delay = ex.ShortCode == "HTTP_429" ? TimeSpan.FromSeconds(10) : TimeSpan.FromSeconds(2);
                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
            }
            catch (UupDumpApiException ex)
            {
                throw;
            }
        }

        throw lastError ?? new UupDumpApiException("UNKNOWN", "UUP dump 服务请求失败，请稍后重试。");
    }

    private static async Task<JsonDocument> GetJsonOnceAsync(string url, CancellationToken ct)
    {
        // 每次请求独立 30 秒上限：单次坏请求不会拖住整个向导（连接层另有 9 秒预算）
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        requestCts.CancelAfter(TimeSpan.FromSeconds(30));

        HttpResponseMessage? response = null;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("TubaWinUi3-UupDump/2.0");
            response = await _http.SendAsync(request, requestCts.Token);

            var json = await response.Content.ReadAsStringAsync(requestCts.Token);
            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                throw new UupDumpApiException("BAD_RESPONSE",
                    $"UUP dump 服务返回了无法解析的数据（HTTP {(int)response.StatusCode}），请稍后重试。");
            }

            var root = doc.RootElement;
            if (!root.TryGetProperty("response", out var resp))
                throw new UupDumpApiException("BAD_RESPONSE", "UUP dump 服务响应格式异常，请稍后重试。");

            if (resp.TryGetProperty("error", out var errEl))
            {
                var code = errEl.GetString() ?? "UNKNOWN";
                throw new UupDumpApiException(code, GetFriendlyErrorMessage(code));
            }

            if (!response.IsSuccessStatusCode)
                throw new UupDumpApiException("HTTP_" + (int)response.StatusCode,
                    $"UUP dump 服务请求失败（HTTP {(int)response.StatusCode}），请稍后重试。");

            return doc;
        }
        catch (HttpRequestException ex)
        {
            throw new UupDumpApiException("NETWORK",
                $"无法连接 UUP dump 服务器（{ApiHost}）：{ex.Message}\n" +
                "请检查网络连接；若开启了代理/VPN，请确认其可用后再重试。");
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            // 30 秒请求上限到期（或底层连接中断）：不是用户取消，按超时处理
            throw new UupDumpApiException("TIMEOUT", "UUP dump 服务响应过慢（30 秒未完成），请稍后重试。");
        }
        finally
        {
            response?.Dispose();
        }
    }

    internal static string GetFriendlyErrorMessage(string code)
    {
        return code switch
        {
            "USER_RATE_LIMITED" => "请求过于频繁，UUP dump 服务暂时限制了本机访问，请等待约 10 秒后重试。",
            "NO_UPDATE_FOUND" => "未在 Windows Update 服务器找到该构建，可能已被微软移除。",
            "UNKNOWN_ARCH" => "不支持的架构类型。",
            "UNKNOWN_RING" => "不支持的更新渠道。",
            "ILLEGAL_BUILD" => "构建号格式不正确。",
            "NO_FILES" => "该版本没有可下载的文件。",
            "EMPTY_FILELIST" => "该构建的文件列表为空，可能已被微软移除。",
            "UNSUPPORTED_COMBO" => "该构建与所选语言/版本组合不受支持。",
            "KEY_NOT_IN_DB" => "该构建信息尚未收录，请稍后重试。",
            _ => $"UUP dump 服务返回错误（{code}），请稍后重试。",
        };
    }

    /// <summary>根据标题归类渠道。「更新包」是累积/预览/组件更新等非完整镜像条目，仅在全部分类中展示。</summary>
    internal static string DeriveChannel(string title)
    {
        var t = title.ToLowerInvariant();
        if (t.Contains("server") || t.Contains("azure stack")) return "Server";
        if (t.Contains("insider") || t.Contains("canary") || t.Contains("dev channel")) return "预览体验版";
        if (t.Contains("update") || t.Contains("experience pack") || t.Contains("language pack"))
            return "更新包";
        return "正式版";
    }

    private static string BuildLanguageDisplay(string code, Dictionary<string, string> fancy)
    {
        var zh = LanguageNames.TryGetValue(code, out var z) ? z : null;
        var en = fancy.TryGetValue(code, out var f) && !string.IsNullOrEmpty(f) ? f : null;
        if (zh is not null && en is not null) return $"{zh} ({en})";
        return zh ?? en ?? code;
    }

    private static string GetString(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return "";
        if (!el.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            _ => "",
        };
    }

    private static long GetInt64(JsonElement el, string name)
    {
        if (el.ValueKind != JsonValueKind.Object) return 0;
        if (!el.TryGetProperty(name, out var v)) return 0;
        return v.ValueKind switch
        {
            JsonValueKind.Number when v.TryGetInt64(out var i) => i,
            JsonValueKind.String when long.TryParse(v.GetString(), out var i) => i,
            _ => 0,
        };
    }
}
