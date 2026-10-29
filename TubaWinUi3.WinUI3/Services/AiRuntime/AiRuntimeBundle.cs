using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace TubaWinUi3.Services.AiRuntime;

/// <summary>
/// 「AI 运行库」清单与解包逻辑（纯逻辑，无网络/UI，可单测）。
/// AI 依赖（Windows ML / ORT GenAI）的运行时 DLL 不随安装包分发：
/// 首次用到 AI 工具时从 NuGet 下载对应 .nupkg（整包就是 zip），按规则解包提取需要的
/// 托管/原生 DLL 到数据目录，再由 <see cref="AiRuntimeLoader"/> 动态加载。
/// </summary>
internal static class AiRuntimeBundle
{
    /// <summary>
    /// 契约版本：包清单/解包规则/依赖版本任一变化时 +1。
    /// 已装运行库的 manifest 版本与此不一致 → 视为未初始化，重新下载安装。
    /// </summary>
    public const string ContractVersion = "2";

    public const string DisplayName = "AI 运行库";

    public const string Glyph = "\uEC4A"; // Segoe Fluent: DeveloperTools

    /// <summary>当前进程架构对应的运行库 RID（非 x64/arm64/x86 返回 null）。</summary>
    public static string? GetRid() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X64 => "win-x64",
        Architecture.Arm64 => "win-arm64",
        Architecture.X86 => "win-x86",
        _ => null
    };

    /// <summary>是否支持本地推理（Windows ML / ORT GenAI 只有 x64 与 arm64 原生资产）。</summary>
    public static bool SupportsLocalInference(string rid) => rid is "win-x64" or "win-arm64";

    /// <summary>单个 NuGet 包（版本必须与 TubaWinUi3.csproj 的 PackageReference 一致）。</summary>
    /// <param name="Id">包 Id（大小写不敏感，URL 用其小写形式）。</param>
    /// <param name="Version">包版本。</param>
    /// <param name="IncludeOnAllArches">true 时 x86 也下载（纯托管包）；false 仅 x64/arm64。</param>
    /// <param name="IncludeNatives">是否提取 runtimes/{rid}/native/*.dll（x86 无原生资产）。</param>
    internal sealed record PackageSpec(string Id, string Version, bool IncludeOnAllArches, bool IncludeNatives);

    /// <summary>托管程序集文件夹优先级：取 nupkg 里第一个命中且含 .dll 的目录。</summary>
    private static readonly string[] LibFolderPreference =
        ["net10.0", "net9.0", "net8.0", "net8.0-windows10.0.17763.0", "netstandard2.0"];

    /// <summary>需要下载的包列表（依进程架构计算）。</summary>
    public static IReadOnlyList<PackageSpec> GetPackages(string rid)
    {
        var packages = new List<PackageSpec>();

        // Windows ML（含 ORT + DirectML 原生库）与它的托管依赖：仅 x64/arm64 需要下载。
        // x86 的 Windows ML 托管程序集随包分发（见 csproj 的 AiRuntimeWinMlExcludeAssets），
        // 且无 32 位原生资产，本地推理由 UnsupportedReason 挡在入口。
        if (SupportsLocalInference(rid))
        {
            packages.Add(new PackageSpec("Microsoft.Windows.AI.MachineLearning", "2.4.89",
                IncludeOnAllArches: false, IncludeNatives: true));
            packages.Add(new PackageSpec("System.Numerics.Tensors", "9.0.0",
                IncludeOnAllArches: false, IncludeNatives: false));
            packages.Add(new PackageSpec("Microsoft.ML.OnnxRuntimeGenAI.WinML", "0.17.1",
                IncludeOnAllArches: false, IncludeNatives: true));
            packages.Add(new PackageSpec("Microsoft.ML.OnnxRuntimeGenAI.Managed", "0.17.1",
                IncludeOnAllArches: false, IncludeNatives: false));
        }

        return packages;
    }

    /// <summary>安装完成后必须存在的文件（逐个缺失即视为未安装）。</summary>
    public static IReadOnlyList<string> GetRequiredFiles(string rid)
    {
        var files = new List<string>();

        if (SupportsLocalInference(rid))
        {
            files.AddRange(
            [
                "Microsoft.ML.OnnxRuntime.dll",
                "Microsoft.Windows.AI.MachineLearning.Projection.dll",
                "System.Numerics.Tensors.dll",
                "onnxruntime.dll",
                "DirectML.dll",
                "Microsoft.Windows.AI.MachineLearning.dll",
                "onnxruntime-genai.dll",
                "Microsoft.ML.OnnxRuntimeGenAI.dll",
            ]);
        }

        return files;
    }

    /// <summary>按 rid 解包一个 .nupkg，把命中的 DLL 平铺复制进 targetDir，返回提取的文件名。</summary>
    public static List<string> ExtractNupkg(string nupkgPath, PackageSpec spec, string rid,
        string targetDir, IProgress<string>? status = null)
    {
        var extracted = new List<string>();
        using var archive = ZipFile.OpenRead(nupkgPath);

        // 1) 托管程序集：按优先级选第一个含 .dll 的 lib 目录
        var libPrefix = FindBestLibPrefix(archive);
        if (libPrefix is not null)
        {
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(libPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                ExtractEntry(entry, targetDir, extracted);
            }
        }

        // 2) 原生库：runtimes/{rid}/native/*.dll（排除没人用的 CUDA 变体）
        if (spec.IncludeNatives)
        {
            var nativePrefix = $"runtimes/{rid}/native/";
            foreach (var entry in archive.Entries)
            {
                if (!entry.FullName.StartsWith(nativePrefix, StringComparison.OrdinalIgnoreCase)) continue;
                if (!entry.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.FullName.Contains("cuda", StringComparison.OrdinalIgnoreCase)) continue;
                ExtractEntry(entry, targetDir, extracted);
            }
        }

        status?.Report($"{spec.Id}：提取 {extracted.Count} 个文件");
        return extracted;
    }

    private static string? FindBestLibPrefix(ZipArchive archive)
    {
        foreach (var folder in LibFolderPreference)
        {
            var prefix = $"lib/{folder}/";
            if (archive.Entries.Any(e =>
                    e.FullName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                    e.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
                return prefix;
        }
        return null;
    }

    private static void ExtractEntry(ZipArchiveEntry entry, string targetDir, List<string> extracted)
    {
        var name = Path.GetFileName(entry.FullName);
        if (string.IsNullOrEmpty(name)) return;

        Directory.CreateDirectory(targetDir);
        var target = Path.Combine(targetDir, name);
        entry.ExtractToFile(target, overwrite: true);
        extracted.Add(name);
    }

    /// <summary>写安装清单（原子写：先写临时文件再替换）。</summary>
    public static void WriteManifest(string runtimeDir, string rid, IReadOnlyList<PackageSpec> packages,
        IReadOnlyList<string> files)
    {
        var manifest = new
        {
            contractVersion = ContractVersion,
            rid,
            installedAt = DateTime.Now.ToString("s"),
            packages = packages.Select(p => $"{p.Id} {p.Version}").ToArray(),
            files = files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToArray(),
        };
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });

        Directory.CreateDirectory(runtimeDir);
        var path = Path.Combine(runtimeDir, "manifest.json");
        var temp = path + ".tmp";
        File.WriteAllText(temp, json);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>读取已安装清单的契约版本；缺失/损坏返回 null。</summary>
    public static string? ReadInstalledContractVersion(string runtimeDir)
    {
        try
        {
            var path = Path.Combine(runtimeDir, "manifest.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            return doc.RootElement.TryGetProperty("contractVersion", out var v) ? v.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>给定目录是否具备全部必备文件。</summary>
    public static bool HasAllRequiredFiles(string dir, string rid)
    {
        if (!Directory.Exists(dir)) return false;
        return GetRequiredFiles(rid).All(f => File.Exists(Path.Combine(dir, f)));
    }
}
