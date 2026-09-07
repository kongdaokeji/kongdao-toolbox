using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services;

public sealed record ToolMetadata(
    string? Description,
    string? Publisher,
    string? Version,
    string? DatabaseSource,
    string? DownloadUrl,
    string? DownloadFilter,
    string? WingetId,
    string? LaunchTarget,
    string? TutorialUrl,
    IReadOnlyList<string>? Tags,
    int? ToolVersion,
    int? Order = null);

public sealed record JsonArchVariantResult(string? File, string? Dir, string? Arch);

public sealed record RemoteToolVersion(string Match, int Version, string? DownloadUrl);

public static class ToolMetadataService
{
    private static IReadOnlyList<JsonToolMetadata>? _metadata;

    public static void InvalidateCache()
    {
        _metadata = null;
    }

    public static async Task RemoveMetadataAsync(string toolPath)
    {
        var dirName = Path.GetFileName(Path.GetDirectoryName(toolPath));
        if (string.IsNullOrWhiteSpace(dirName)) return;

        var metadataRoot = GetWritableMetadataDir();
        var metadataPath = Path.Combine(metadataRoot, "tools.json");
        if (!File.Exists(metadataPath)) return;

        JsonObject root;
        JsonArray tools;

        await using (var readStream = File.OpenRead(metadataPath))
        {
            root = await JsonNode.ParseAsync(readStream) as JsonObject ?? new JsonObject();
        }

        tools = root["tools"] as JsonArray ?? [];
        var existing = tools
            .OfType<JsonObject>()
            .FirstOrDefault(item =>
                string.Equals(item["match"]?.GetValue<string>(), dirName, StringComparison.CurrentCultureIgnoreCase));

        if (existing is null) return;

        tools.Remove(existing);
        root["tools"] = tools;

        await using var writeStream = File.Create(metadataPath);
        await JsonSerializer.SerializeAsync(writeStream, root, new JsonSerializerOptions { WriteIndented = true });
        _metadata = null;
    }

    public static bool HasDownloadUrl(string category, string toolDir)
    {
        var dirName = Path.GetFileName(toolDir);
        var metadata = LoadMetadata();

        return metadata.Any(item =>
            !string.IsNullOrWhiteSpace(item.Match) &&
            (!string.IsNullOrWhiteSpace(item.DownloadUrl) || !string.IsNullOrWhiteSpace(item.WingetId)) &&
            dirName.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase));
    }

    public static ToolMetadata GetMetadata(string category, string toolPath)
    {
        var jsonMetadata = FindJsonMetadata(toolPath);

        string? description = jsonMetadata?.Description;
        string? publisher = jsonMetadata?.Publisher;
        string? version = null;

        if (File.Exists(toolPath))
        {
            try
            {
                var ext = Path.GetExtension(toolPath);
                if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                    ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    var versionInfo = FileVersionInfo.GetVersionInfo(toolPath);
                    description ??= FirstUseful(versionInfo.FileDescription, versionInfo.ProductName);
                    publisher ??= FirstUseful(versionInfo.CompanyName, versionInfo.LegalCopyright);
                    version = FirstUseful(versionInfo.ProductVersion, versionInfo.FileVersion);
                }
            }
            catch { }
        }

        if (description is null)
        {
            description = ReadFolderDescription(toolPath);
        }

        return new ToolMetadata(
            description,
            publisher,
            version,
            jsonMetadata is null ? null : "JSON",
            jsonMetadata?.DownloadUrl,
            jsonMetadata?.DownloadFilter,
            jsonMetadata?.WingetId,
            jsonMetadata?.LaunchTarget,
            jsonMetadata?.TutorialUrl,
            jsonMetadata?.Tags,
            jsonMetadata?.ToolVersion,
            jsonMetadata?.Order);
    }

    public static IReadOnlyList<JsonArchVariantResult> GetArchVariants(string toolPath, string? toolDir = null)
    {
        var jsonMetadata = FindJsonMetadata(toolPath);
        if (jsonMetadata is null && toolDir is not null)
            jsonMetadata = FindJsonMetadataByDir(toolDir);

        if (jsonMetadata?.ArchVariants is null || jsonMetadata.ArchVariants.Count == 0)
            return [];

        return jsonMetadata.ArchVariants
            .Select(v => new JsonArchVariantResult(v.File, v.Dir, v.Arch))
            .ToList();
    }

    private static JsonToolMetadata? FindJsonMetadata(string toolPath)
    {
        var metadata = LoadMetadata();
        var fileName = Path.GetFileNameWithoutExtension(toolPath);
        var relativePath = Path.GetRelativePath(ToolCatalog.ToolsRoot, toolPath);
        var dirName = Path.GetFileName(Path.GetDirectoryName(toolPath));

        return metadata
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Match) &&
                (fileName.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase) ||
                 relativePath.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase) ||
                 MatchesFlexible(dirName, item.Match)))
            .OrderByDescending(item => item.Match!.Length)
            .FirstOrDefault();
    }

    public static string? GetLaunchTarget(string toolDir)
    {
        var jsonMetadata = FindJsonMetadataByDir(toolDir);
        return jsonMetadata?.LaunchTarget;
    }

    public static int? GetToolVersion(string toolPath)
    {
        var jsonMetadata = FindJsonMetadata(toolPath);
        return jsonMetadata?.ToolVersion;
    }

    public static int? GetToolVersionByDir(string toolDir)
    {
        var jsonMetadata = FindJsonMetadataByDir(toolDir);
        return jsonMetadata?.ToolVersion;
    }

    public static string? GetDownloadUrlByDir(string toolDir)
    {
        var jsonMetadata = FindJsonMetadataByDir(toolDir);
        return jsonMetadata?.DownloadUrl;
    }

    public static void UpdateToolVersion(string match, int newVersion)
    {
        try
        {
            var metadataPath = Path.Combine(GetWritableMetadataDir(), "tools.json");
            if (!File.Exists(metadataPath)) return;

            var jsonText = File.ReadAllText(metadataPath);
            var doc = JsonNode.Parse(jsonText);
            if (doc?["tools"] is not JsonArray tools) return;

            foreach (var tool in tools)
            {
                var m = tool?["match"]?.ToString();
                if (m is not null && m.Equals(match, StringComparison.OrdinalIgnoreCase))
                {
                    tool!["version"] = newVersion;
                }
            }

            File.WriteAllText(metadataPath, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            _metadata = null;
        }
        catch { }
    }

    /// <summary>
    /// 把卡片拖拽排序结果写回 tools.json 的 order 字段。
    /// orderedToolDirs 为工具目录（按期望顺序）；未收录进 tools.json 的自定义工具自动跳过。
    /// 读取侧（FindJsonMetadataByDir → Order）与写入侧使用同一套匹配规则，保证读写一致。
    /// </summary>
    public static void SaveToolOrder(IReadOnlyList<string> orderedToolDirs)
    {
        try
        {
            var metadataPath = Path.Combine(GetWritableMetadataDir(), "tools.json");
            if (!File.Exists(metadataPath) || orderedToolDirs.Count == 0) return;

            var doc = JsonNode.Parse(File.ReadAllText(metadataPath));
            if (doc?["tools"] is not JsonArray tools) return;

            var order = 0;
            foreach (var dir in orderedToolDirs)
            {
                var match = FindJsonMetadataByDir(dir)?.Match;
                if (string.IsNullOrWhiteSpace(match)) continue;

                var entry = tools
                    .OfType<JsonObject>()
                    .FirstOrDefault(item => string.Equals(item["match"]?.GetValue<string>(), match, StringComparison.CurrentCultureIgnoreCase));
                if (entry is null) continue;

                entry["order"] = order++;
            }

            File.WriteAllText(metadataPath, doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            _metadata = null; // 立即失效内存缓存，下次读取即为新顺序
        }
        catch { }
    }

    public static async Task<IReadOnlyList<RemoteToolVersion>?> FetchRemoteToolsJsonAsync(CancellationToken ct = default)
    {
        try
        {
            var result = await TryFetchGitCodeToolsJsonAsync(ct);
            if (result is not null) return result;
        }
        catch { }

        try
        {
            var result = await TryFetchGitHubToolsJsonAsync(ct);
            if (result is not null) return result;
        }
        catch { }

        return null;
    }

    private static async Task<IReadOnlyList<RemoteToolVersion>?> TryFetchGitCodeToolsJsonAsync(CancellationToken ct)
    {
        const string url = "https://raw.gitcode.com/luolangaga/tubatool/raw/master/TubaWinUi3.WinUI3/Metadata/tools.json";

        using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(15));
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolUpdate");

        var toolsJsonText = await client.GetStringAsync(url, ct);
        return ParseRemoteToolsJson(toolsJsonText);
    }

    private static async Task<IReadOnlyList<RemoteToolVersion>?> TryFetchGitHubToolsJsonAsync(CancellationToken ct)
    {
        const string url = "https://raw.githubusercontent.com/luolangaga/tubatool/master/TubaWinUi3.WinUI3/Metadata/tools.json";

        using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(15));
        if (!client.DefaultRequestHeaders.Contains("User-Agent"))
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ToolUpdate");

        var toolsJsonText = await client.GetStringAsync(url, ct);
        return ParseRemoteToolsJson(toolsJsonText);
    }

    private static IReadOnlyList<RemoteToolVersion>? ParseRemoteToolsJson(string jsonText)
    {
        try
        {
            var database = JsonSerializer.Deserialize<JsonToolDatabase>(jsonText, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            if (database?.Tools is null) return null;

            return database.Tools
                .Where(t => !string.IsNullOrWhiteSpace(t.Match) && t.ToolVersion.HasValue)
                .Select(t => new RemoteToolVersion(t.Match!, t.ToolVersion!.Value, t.DownloadUrl))
                .ToList();
        }
        catch { return null; }
    }

    private static JsonToolMetadata? FindJsonMetadataByDir(string toolDir)
    {
        var metadata = LoadMetadata();
        var dirName = Path.GetFileName(toolDir);
        var relativePath = Path.GetRelativePath(ToolCatalog.ToolsRoot, toolDir);

        return metadata
            .Where(item =>
                !string.IsNullOrWhiteSpace(item.Match) &&
                (relativePath.Contains(item.Match, StringComparison.CurrentCultureIgnoreCase) ||
                 MatchesFlexible(dirName, item.Match)))
            .OrderByDescending(item => item.Match!.Length)
            .FirstOrDefault();
    }

    /// <summary>目录名/路径与 tools.json match 字段的灵活匹配规则（去空格-下划线-连字符后子串匹配），供目录定位复用。</summary>
    public static bool MatchesFlexible(string? source, string match)
    {
        if (string.IsNullOrWhiteSpace(source))
            return false;

        if (source.Contains(match, StringComparison.CurrentCultureIgnoreCase))
            return true;

        var normalizedSource = source.Replace(" ", "", StringComparison.Ordinal)
                                      .Replace("-", "", StringComparison.Ordinal)
                                      .Replace("_", "", StringComparison.Ordinal);
        var normalizedMatch = match.Replace(" ", "", StringComparison.Ordinal)
                                   .Replace("-", "", StringComparison.Ordinal)
                                   .Replace("_", "", StringComparison.Ordinal);

        return normalizedSource.Contains(normalizedMatch, StringComparison.CurrentCultureIgnoreCase);
    }

    /// <summary>
    /// 多分类副本/内置挂载声明：tools.json 条目的 categories 含指定分类时返回该条目。
    /// 真实工具副本用 category(主分类)+categories(副本分类)；内置挂载用 builtin+categories(挂载位置)。
    /// </summary>
    public static IReadOnlyList<CategoryPlacement> GetCategoryPlacements(string category)
    {
        try
        {
            return LoadMetadata()
                .Where(item => item.Categories is not null &&
                               item.Categories.Any(c => string.Equals(c, category, StringComparison.OrdinalIgnoreCase)))
                .Select(item => new CategoryPlacement(
                    item.Match ?? "",
                    item.Category,
                    item.Categories!.Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    string.IsNullOrWhiteSpace(item.Builtin) ? null : item.Builtin,
                    item.Order))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public sealed record CategoryPlacement(
        string Match,
        string? PrimaryCategory,
        IReadOnlyList<string> Categories,
        string? BuiltinId,
        int? Order);

    private static IReadOnlyList<JsonToolMetadata> LoadMetadata()
    {
        if (_metadata is not null)
        {
            return _metadata;
        }

        var path = Path.Combine(GetWritableMetadataDir(), "tools.json");
        if (!File.Exists(path))
        {
            _metadata = [];
            return _metadata;
        }

        using var stream = File.OpenRead(path);
        var database = JsonSerializer.Deserialize<JsonToolDatabase>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        _metadata = database?.Tools ?? [];
        return _metadata;
    }

    private static string? ReadFolderDescription(string toolPath)
    {
        var directory = Path.GetDirectoryName(toolPath);
        if (directory is null)
        {
            return null;
        }

        var textFile = Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetFileName(path).Contains("readme", StringComparison.OrdinalIgnoreCase) ||
                                    Path.GetFileName(path).Contains("说明", StringComparison.CurrentCultureIgnoreCase) ||
                                    Path.GetFileName(path).Contains("What's New", StringComparison.OrdinalIgnoreCase));
        if (textFile is null)
        {
            return null;
        }

        try
        {
            var text = File.ReadLines(textFile).FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
            return text is { Length: > 160 } ? text[..160] : text;
        }
        catch
        {
            return null;
        }
    }

    private static string? FirstUseful(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
    }

    /// <summary>仅供测试使用：直接替代 FindRoot 的返回值（即 Metadata 目录本身，null = 恢复自动查找）。</summary>
    internal static string? MetadataRootOverride;

    internal static void SetMetadataRootForTests(string? metadataDir)
    {
        MetadataRootOverride = metadataDir;
        _metadata = null;
    }

    private static string FindRoot(string folderName)
    {
        if (MetadataRootOverride is not null)
            return MetadataRootOverride;

        var appDir = ToolCatalog.AppDirectory;
        var outputRoot = Path.Combine(appDir, folderName);
        if (Directory.Exists(outputRoot))
        {
            return outputRoot;
        }

        var directory = new DirectoryInfo(appDir);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, folderName);
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return outputRoot;
    }

    public static string GetWritableMetadataDir()
    {
        if (!RuntimeHelper.IsMsixPackaged)
            return FindRoot("Metadata");

        var writableDir = Path.Combine(
            RuntimeHelper.GetLocalAppDataRoot(),
            "TubaWinUi3", "Metadata");

        if (!Directory.Exists(writableDir))
        {
            var installDir = FindRoot("Metadata");
            if (Directory.Exists(installDir))
            {
                Directory.CreateDirectory(writableDir);
                foreach (var file in Directory.EnumerateFiles(installDir))
                {
                    try
                    {
                        var dest = Path.Combine(writableDir, Path.GetFileName(file));
                        if (!File.Exists(dest))
                            File.Copy(file, dest, false);
                    }
                    catch { }
                }
            }
            else
            {
                Directory.CreateDirectory(writableDir);
            }
        }

        return writableDir;
    }

    private sealed class JsonToolDatabase
    {
        public List<JsonToolMetadata> Tools { get; set; } = [];
    }

    private sealed class JsonToolMetadata
    {
        public string? Match { get; set; }
        public string? Description { get; set; }
        public string? Publisher { get; set; }
        public string? DownloadUrl { get; set; }
        public string? DownloadFilter { get; set; }
        public string? WingetId { get; set; }
        public string? LaunchTarget { get; set; }
        public string? TutorialUrl { get; set; }
        public List<string>? Tags { get; set; }
        public List<JsonArchVariant>? ArchVariants { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("version")]
        public int? ToolVersion { get; set; }
        public int? Order { get; set; }

        /// <summary>主分类：物理目录所在的分类（多分类副本条目声明用）。</summary>
        public string? Category { get; set; }

        /// <summary>副本分类：工具额外出现的分类列表；内置挂载条目 = 挂载位置列表。</summary>
        public List<string>? Categories { get; set; }

        /// <summary>内置工具挂载：BuiltinToolRegistry 的工具 id（替代旧 link.json 的 builtin 链接）。</summary>
        public string? Builtin { get; set; }
    }

    private sealed class JsonArchVariant
    {
        public string? File { get; set; }

        public string? Dir { get; set; }

        public string? Arch { get; set; }
    }
}
