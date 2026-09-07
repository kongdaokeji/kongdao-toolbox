using System.IO.Compression;

namespace TubaWinUi3.Services;

/// <summary>
/// 转换计划与命令行构造（纯逻辑，可单元测试）。
/// 负责把「源文件 + 目标格式 + 压缩选项」翻译成 FFmpeg / ImageMagick 命令
/// 与输出路径命名。
/// </summary>
public static class FormatConvertPlanner
{
    /// <summary>老版二进制文档格式，纯轻量引擎无法解析（提示另存为新格式）。</summary>
    private static readonly HashSet<string> LegacyDocExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".ppt"
    };

    /// <summary>.doc / .ppt 老格式不支持，返回 true（页面提示用户另存为 docx/pptx）。</summary>
    public static bool IsLegacyDoc(string filePath)
        => LegacyDocExtensions.Contains(Path.GetExtension(filePath));

    /// <summary>
    /// 构造 FFmpeg 参数（视频/音频；含提取音频与 GIF 分支）。
    /// </summary>
    /// <param name="videoWidth">输出最长边（0 = 不缩放；GIF 时作为宽度）。</param>
    /// <param name="sampleRate">输出采样率 Hz（0 = 保持不变）。</param>
    /// <param name="channels">输出声道数（0 = 保持不变）。</param>
    public static string BuildFfmpegArgs(string source, FormatOption target, int crf, string preset,
        int audioBitrateKbps, bool compress, int videoWidth = 0, int sampleRate = 0, int channels = 0)
    {
        var output = BuildOutputPath(source, target.Ext);

        // 纯音频输出（视频提取音频 / 音频转码）
        if (target.IsAudioOnly)
        {
            var args = $"-i \"{source}\" -vn -c:a {target.DefaultACodec}";
            if (compress && audioBitrateKbps > 0)
                args += $" -b:a {audioBitrateKbps}k";
            if (sampleRate > 0)
                args += $" -ar {sampleRate}";
            if (channels > 0)
                args += $" -ac {channels}";
            args += $" \"{output}\"";
            return args;
        }

        // GIF 动图（filter_complex 内生成调色板 + 应用，单次完成，质量高且避免编码器崩溃）
        if (target.Ext == ".gif")
        {
            var w = videoWidth > 0 ? videoWidth : 480;
            var vfBase = $"fps=15,scale={w}:-1:flags=lanczos";
            return $"-i \"{source}\" -filter_complex \"[0:v] {vfBase},split [a][b];[b] palettegen=stats_mode=diff [p];[a][p] paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle\" -an \"{output}\"";
        }

        // 常规视频转码
        var sb = new System.Text.StringBuilder();
        sb.Append($"-i \"{source}\" -c:v {target.DefaultVCodec}");
        if (compress)
        {
            sb.Append($" -crf {crf} -preset {preset}");
            if (audioBitrateKbps > 0)
                sb.Append($" -b:a {audioBitrateKbps}k");
        }
        // 最长边缩放（保持比例，force_original_aspect_ratio 只缩小；第二个 scale 保证偶数尺寸）
        if (videoWidth > 0)
            sb.Append($" -vf \"scale={videoWidth}:{videoWidth}:force_original_aspect_ratio=decrease,scale=trunc(iw/2)*2:trunc(ih/2)*2\"");
        if (sampleRate > 0)
            sb.Append($" -ar {sampleRate}");
        if (channels > 0)
            sb.Append($" -ac {channels}");
        sb.Append($" -c:a {target.DefaultACodec} \"{output}\"");
        return sb.ToString();
    }

    /// <summary>
    /// GIF 降级参数：最简单的 -vf 路径，不使用 filter_complex，
    /// 用于 palette 方式导致 FFmpeg 崩溃时的兜底重试。
    /// </summary>
    public static string BuildFfmpegGifFallbackArgs(string source, int videoWidth)
    {
        var output = BuildOutputPath(source, ".gif");
        var w = videoWidth > 0 ? videoWidth : 480;
        return $"-i \"{source}\" -vf \"fps=15,scale={w}:-1\" -an \"{output}\"";
    }

    /// <summary>FFmpeg 退出码是否表示进程崩溃（非正常错误退出）。</summary>
    public static bool IsFfmpegCrash(int exitCode) => exitCode < 0 || exitCode > 125;

    /// <summary>
    /// 构造 ImageMagick 参数（格式转换 + 压缩：质量/strip/最长边缩放）。
    /// </summary>
    /// <param name="icoSizes">ICO 目标的多尺寸列表（icon:auto-resize），为空时默认 256,128,64,48,32,16。</param>
    public static string BuildMagickArgs(string source, FormatOption target, int quality,
        int maxDimension, bool compress, int[]? icoSizes = null)
    {
        var output = BuildOutputPath(source, target.Ext);
        var sb = new System.Text.StringBuilder();
        sb.Append($"\"{source}\"");

        if (compress)
        {
            if (quality > 0)
                sb.Append($" -quality {quality}");
            sb.Append(" -strip");
            if (maxDimension > 0 && target.Ext != ".ico")
                sb.Append($" -resize {maxDimension}x{maxDimension}>");
        }

        // ICO 支持在同一文件内包含多个尺寸（Windows 图标标准做法），
        // 超出 256x256 的源由 icon:auto-resize 自动缩小
        if (target.Ext == ".ico")
        {
            var sizes = icoSizes is { Length: > 0 }
                ? icoSizes
                : new[] { 256, 128, 64, 48, 32, 16 };
            sb.Append($" -define icon:auto-resize={string.Join(",", sizes)}");
        }

        sb.Append($" \"{output}\"");
        return sb.ToString();
    }

    /// <summary>
    /// 构造「图片 → MP4 / WebM 视频」的 FFmpeg 参数。
    /// 动图（GIF）按原有帧序列转码；静态图片以 -loop 1 循环展示指定时长。
    /// </summary>
    /// <param name="durationSeconds">静态图片的视频时长（默认 5 秒，1-600）。</param>
    /// <param name="maxEdge">最长边缩放（0 = 不缩放），并强制偶数尺寸（H.264/VP9 要求）。</param>
    public static string BuildImageVideoArgs(string source, FormatOption target,
        int durationSeconds, int maxEdge)
    {
        var output = BuildOutputPath(source, target.Ext);
        var dur = Math.Clamp(durationSeconds <= 0 ? 5 : durationSeconds, 1, 600);
        var isAnimated = Path.GetExtension(source).Equals(".gif", StringComparison.OrdinalIgnoreCase);

        var scale = maxEdge > 0
            ? $"scale={maxEdge}:{maxEdge}:force_original_aspect_ratio=decrease,scale=trunc(iw/2)*2:trunc(ih/2)*2"
            : "scale=trunc(iw/2)*2:trunc(ih/2)*2";

        var sb = new System.Text.StringBuilder();
        if (!isAnimated)
            sb.Append($"-loop 1 -t {dur} ");
        sb.Append($"-i \"{source}\" -c:v {target.DefaultVCodec} -pix_fmt yuv420p -vf \"{scale}\" -r 30 -an");
        if (target.Ext == ".mp4")
        {
            if (!isAnimated)
                sb.Append(" -tune stillimage");
            sb.Append(" -movflags +faststart");
        }
        else if (target.Ext == ".webm")
        {
            sb.Append(" -b:v 0 -crf 32");
        }
        sb.Append($" \"{output}\"");
        return sb.ToString();
    }

    /// <summary>ZIP 压缩级别（0-9）映射到 .NET 的 CompressionLevel（粒度折算）。</summary>
    public static System.IO.Compression.CompressionLevel ZipCompressionLevel(int level0To9)
    {
        return level0To9 switch
        {
            <= 0 => System.IO.Compression.CompressionLevel.NoCompression,
            <= 3 => System.IO.Compression.CompressionLevel.Fastest,
            <= 7 => System.IO.Compression.CompressionLevel.Optimal,
            _ => System.IO.Compression.CompressionLevel.SmallestSize
        };
    }

    /// <summary>
    /// 多张图片合成为一份 PDF 的 ImageMagick 参数（多个输入按顺序拼接为多页 PDF）。
    /// </summary>
    public static string BuildMagickMergePdfArgs(IReadOnlyList<string> sources, string outputPath,
        int quality, int maxDimension, bool compress)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var source in sources)
            sb.Append($"\"{source}\" ");

        if (compress)
        {
            if (quality > 0)
                sb.Append($"-quality {quality}");
            sb.Append(" -strip");
            if (maxDimension > 0)
                sb.Append($" -resize {maxDimension}x{maxDimension}>");
        }

        sb.Append($"\"{outputPath}\"");
        return sb.ToString();
    }

    /// <summary>把一批文件打包为 ZIP（文件名平铺，重名时带上级目录名消歧）。
    /// 返回（压缩前总大小, 压缩后大小）。zipPath 由调用方保证不冲突。</summary>
    public static (long BeforeBytes, long AfterBytes) CreateZipArchive(
        IReadOnlyList<string> files, string zipPath, int level0To9)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(zipPath) ?? ".");
        long before = 0;
        using (var archive = System.IO.Compression.ZipFile.Open(
                   zipPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var level = ZipCompressionLevel(level0To9);
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                before += new FileInfo(file).Length;
                var entryName = Path.GetFileName(file);
                if (!used.Add(entryName))
                {
                    var folder = Path.GetFileName(Path.GetDirectoryName(file));
                    if (!string.IsNullOrEmpty(folder) && used.Add($"{folder}_{entryName}"))
                        entryName = $"{folder}_{entryName}";
                    else
                        entryName = $"{Path.GetFileNameWithoutExtension(file)}_{Guid.NewGuid().ToString("N")[..8]}{Path.GetExtension(file)}";
                }
                archive.CreateEntryFromFile(file, entryName, level);
            }
        }
        return (before, new FileInfo(zipPath).Length);
    }

    /// <summary>多文件 ZIP 输出路径：源目录下「首个文件名_压缩包.zip」，冲突时追加序号。</summary>
    public static string BuildZipOutputPath(IReadOnlyList<string> sources)
    {
        var first = sources[0];
        var dir = Path.GetDirectoryName(first) ?? ".";
        var baseName = sources.Count == 1
            ? Path.GetFileNameWithoutExtension(first)
            : Path.GetFileNameWithoutExtension(first) + "_压缩包";
        var ext = ".zip";

        static bool Occupied(string p) => File.Exists(p) && new FileInfo(p).Length > 0;
        var basePath = Path.Combine(dir, $"{baseName}{ext}");
        if (!Occupied(basePath)) return basePath;
        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{baseName}_{i}{ext}");
            if (!Occupied(candidate)) return candidate;
        }
        return Path.Combine(dir, $"{baseName}_{Guid.NewGuid():N}{ext}");
    }

    /// <summary>生成不冲突的输出路径：源目录下「原名_converted.ext」，已存在则追加序号。
    /// 0 字节的残留文件视为不存在（可覆盖复用，避免被旧失败产物占用名称）。</summary>
    public static string BuildOutputPath(string source, string targetExt)
    {
        var dir = Path.GetDirectoryName(source) ?? ".";
        var name = Path.GetFileNameWithoutExtension(source);
        var ext = targetExt.StartsWith('.') ? targetExt : "." + targetExt;

        static bool Occupied(string p) => File.Exists(p) && new FileInfo(p).Length > 0;

        var basePath = Path.Combine(dir, $"{name}_converted{ext}");
        if (!Occupied(basePath)) return basePath;

        for (int i = 1; i < 1000; i++)
        {
            var candidate = Path.Combine(dir, $"{name}_converted_{i}{ext}");
            if (!Occupied(candidate)) return candidate;
        }
        return Path.Combine(dir, $"{name}_converted_{Guid.NewGuid():N}{ext}");
    }

    /// <summary>文档转图片时为多页输出生成的路径（单页用原名，多页加 _第N页）。</summary>
    public static string BuildDocPagePath(string dir, string baseName, string ext, int pageIndex, int pageCount)
    {
        var fileName = pageCount <= 1
            ? $"{baseName}{ext}"
            : $"{baseName}_第{pageIndex}页{ext}";
        return Path.Combine(dir, fileName);
    }
}