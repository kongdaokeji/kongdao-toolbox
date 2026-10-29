/* Junk rule database management for the 垃圾清理 builtin tool.
   Two databases are bundled with the app (Assets/JunkCleaner/) and can be
   updated manually from the original FluentCleaner repository:
     - Winapp2.ini  : file/registry cleaning rules (CC-BY-SA-4.0, winapp2 project)
     - Winappx.ini  : preinstalled Appx bloatware removal list
   The data-dir copy always wins; the bundled file is the fallback so the
   tool works offline out of the box. */

using System.Net.Http;
using System.Text;

namespace TubaWinUi3.Services;

public enum JunkDatabaseKind { Winapp2, Winappx }

public sealed record JunkDatabaseInfo(
    JunkDatabaseKind Kind,
    string FileName,
    string Version,
    int EntryCount,
    long FileSizeBytes,
    DateTime UpdatedAt,
    bool IsBundled,
    string EffectivePath);

public static class JunkCleanerDatabase
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    // Original repo (builtbybel/FluentCleaner) ships both databases at the repo root.
    public const string RepoUrl = "https://github.com/builtbybel/FluentCleaner";

    public static string DatabaseDir => Path.Combine(ConfigManager.GetDataDir(), "JunkCleaner");
    public static string CustomDir => Path.Combine(DatabaseDir, "Custom");

    private static string DataPathOf(JunkDatabaseKind kind) =>
        Path.Combine(DatabaseDir, kind == JunkDatabaseKind.Winapp2 ? "winapp2.ini" : "winappx.ini");

    private static string BundledPathOf(JunkDatabaseKind kind) =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "JunkCleaner",
            kind == JunkDatabaseKind.Winapp2 ? "Winapp2.ini" : "Winappx.ini");

    /// <summary>The file actually used: a manually updated copy in the data dir
    /// takes precedence; otherwise the copy bundled with the app.</summary>
    public static string GetEffectivePath(JunkDatabaseKind kind)
    {
        var data = DataPathOf(kind);
        return File.Exists(data) ? data : BundledPathOf(kind);
    }

    public static bool Exists(JunkDatabaseKind kind) => File.Exists(GetEffectivePath(kind));

    public static string FileNameOf(JunkDatabaseKind kind) =>
        kind == JunkDatabaseKind.Winapp2 ? "Winapp2.ini" : "Winappx.ini";

    // --- Info -----------------------------------------------------

    public static JunkDatabaseInfo? GetInfo(JunkDatabaseKind kind)
    {
        var path = GetEffectivePath(kind);
        if (!File.Exists(path)) return null;

        try
        {
            var fi = new FileInfo(path);
            var version = "";
            var entryCount = 0;

            // Winapp2 header: "; Version: 260828" + "; # of entries: 4,075".
            // Winappx has no version header; the entry count is the number of [Section] blocks.
            foreach (var line in File.ReadLines(path).Take(20))
            {
                var t = line.Trim();
                if (version.Length == 0 && t.StartsWith("; Version:", StringComparison.OrdinalIgnoreCase))
                    version = t["; Version:".Length..].Trim();
                if (entryCount == 0 && t.StartsWith("; # of entries:", StringComparison.OrdinalIgnoreCase))
                    int.TryParse(t["; # of entries:".Length..].Trim().Replace(",", ""), out entryCount);
            }

            if (entryCount == 0)
            {
                foreach (var line in File.ReadLines(path))
                {
                    var t = line.Trim();
                    if (t.StartsWith('[') && t.EndsWith(']')) entryCount++;
                }
            }

            var bundled = path == BundledPathOf(kind);
            return new JunkDatabaseInfo(kind, FileNameOf(kind), version, entryCount,
                fi.Length, fi.LastWriteTime, bundled, path);
        }
        catch
        {
            return null;
        }
    }

    // --- Update from the original repo ------------------------------

    /// <summary>Downloads the latest database from the FluentCleaner repo and
    /// stores it in the data dir (which then takes precedence over the bundled copy).
    /// Reports human-readable progress. Throws on total failure.
    /// Returns the info of the effective database afterwards (or null if unchanged).</summary>
    public static async Task<JunkDatabaseInfo> UpdateFromRepoAsync(
        JunkDatabaseKind kind, IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var name = FileNameOf(kind);
        progress?.Report($"正在连接规则库源（builtbybel/FluentCleaner）更新 {name}...");

        Exception? lastError = null;
        foreach (var url in BuildDownloadUrls(kind))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                progress?.Report($"正在下载规则库：{url}");
                using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                resp.EnsureSuccessStatusCode();

                long? total = resp.Content.Headers.ContentLength;
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);

                // Download to a temp file first so a broken transfer never clobbers the DB.
                Directory.CreateDirectory(DatabaseDir);
                var targetPath = DataPathOf(kind);
                var tmpPath = targetPath + ".downloading";
                long received = 0;
                var buffer = new byte[81920];

                await using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    int read;
                    while ((read = await stream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                        received += read;
                        if (total is > 0)
                            progress?.Report($"下载中 {received / 1024.0:F0} KB / {total.Value / 1024.0:F0} KB ({received * 100 / total.Value}%)");
                    }
                }

                // Validate the payload before replacing anything.
                var preview = ReadHead(tmpPath, 4 * 1024);
                if (!ValidatePreview(kind, preview, received))
                {
                    File.Delete(tmpPath);
                    throw new InvalidDataException($"下载内容不是有效的 {name} 规则库");
                }

                // Only replace when the downloaded version is actually newer
                // (databases without a version header always overwrite).
                var newVersion = ParseVersionHeader(preview);
                var oldInfo = GetInfo(kind);
                if (oldInfo is not null && !oldInfo.IsBundled &&
                    newVersion.Length > 0 && oldInfo.Version.Length > 0 &&
                    string.CompareOrdinal(newVersion, oldInfo.Version) <= 0)
                {
                    File.Delete(tmpPath);
                    progress?.Report($"{name} 已是最新（版本 {oldInfo.Version}）");
                    return oldInfo;
                }

                File.Move(tmpPath, targetPath, overwrite: true);
                var info = GetInfo(kind)!;
                progress?.Report($"{name} 已更新" + (info.Version.Length > 0 ? $"到版本 {info.Version}" : ""));
                return info;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastError = ex;
                progress?.Report($"下载失败：{ex.Message}，尝试下一个源...");
            }
        }

        throw new InvalidOperationException($"{name} 更新失败：{lastError?.Message}", lastError);
    }

    /// <summary>Updates both bundled databases (Winapp2 first, then Winappx).</summary>
    public static async Task<(JunkDatabaseInfo Winapp2, JunkDatabaseInfo Winappx)> UpdateAllFromRepoAsync(
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var w2 = await UpdateFromRepoAsync(JunkDatabaseKind.Winapp2, progress, ct);
        var wx = await UpdateFromRepoAsync(JunkDatabaseKind.Winappx, progress, ct);
        return (w2, wx);
    }

    private static string[] BuildDownloadUrls(JunkDatabaseKind kind)
    {
        var file = FileNameOf(kind);
        var raw = $"https://raw.githubusercontent.com/builtbybel/FluentCleaner/main/{file}";
        return
        [
            raw,
            $"https://cdn.jsdelivr.net/gh/builtbybel/FluentCleaner@main/{file}",
            $"https://gh-proxy.com/{raw}",
        ];
    }

    private static bool ValidatePreview(JunkDatabaseKind kind, string preview, long received) => kind switch
    {
        // A real winapp2 database is large and full of FileKey entries.
        JunkDatabaseKind.Winapp2 => received >= 128 * 1024 &&
            preview.Contains("FileKey", StringComparison.OrdinalIgnoreCase),
        // Winappx is a small bloatware list built around PackageName= lines.
        JunkDatabaseKind.Winappx => received >= 1024 &&
            preview.Contains("PackageName", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private static string ReadHead(string path, int bytes)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var buf = new byte[Math.Min(bytes, fs.Length)];
        var read = fs.Read(buf, 0, buf.Length);
        return Encoding.UTF8.GetString(buf, 0, read);
    }

    private static string ParseVersionHeader(string head)
    {
        foreach (var line in head.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("; Version:", StringComparison.OrdinalIgnoreCase))
                return t["; Version:".Length..].Trim();
        }
        return "";
    }
}
