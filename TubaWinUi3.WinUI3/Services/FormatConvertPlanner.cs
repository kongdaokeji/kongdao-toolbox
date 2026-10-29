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
    /// 构造 FFmpeg 参数（视频 / 音频 / GIF），参数全部来自目标格式的专属选项。
    /// </summary>
    public static string BuildFfmpegArgs(string source, FormatOption target, FormatParamValues? values = null)
    {
        var p = values ?? FormatParamValues.Empty;
        var output = BuildOutputPath(source, target.Ext);

        if (target.IsAudioOnly)
            return BuildAudioArgs(source, output, target, p);
        if (target.Ext == ".gif")
            return BuildGifArgs(source, output, p);
        return BuildVideoArgs(source, output, target, p);
    }

    /// <summary>纯音频输出（视频提取音频 / 音频转码）：位深、VBR 质量、压缩级别等按目标格式取值。</summary>
    private static string BuildAudioArgs(string source, string output, FormatOption target, FormatParamValues p)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"-i \"{source}\" -vn");

        var codec = ResolveAudioCodec(target, p);
        sb.Append($" -c:a {codec}");

        var mode = p.GetInt(FormatParamIds.AudioRateMode, 0);
        var bitrate = p.GetInt(FormatParamIds.AudioBitrate, 0);
        var quality = p.GetInt(FormatParamIds.AudioQuality, -1);

        switch (codec)
        {
            case "libmp3lame":
                if (mode == 1)
                    sb.Append($" -q:a {Math.Clamp(quality < 0 ? 2 : quality, 0, 9)}");
                else if (bitrate > 0)
                    sb.Append($" -b:a {bitrate}k");
                break;
            case "libvorbis":
                sb.Append($" -q:a {Math.Clamp(quality < 0 ? 5 : quality, 0, 10)}");
                break;
            case "libopus":
                if (bitrate > 0) sb.Append($" -b:a {bitrate}k");
                sb.Append($" -application {OpusApplicationName(p.GetInt(FormatParamIds.AudioOpusApplication, 0))}");
                break;
            case "flac":
                var level = p.GetInt(FormatParamIds.AudioCompressionLevel, -1);
                if (level >= 0) sb.Append($" -compression_level {Math.Clamp(level, 0, 12)}");
                var depth = p.GetInt(FormatParamIds.AudioBitDepth, 0);
                if (depth >= 3) sb.Append(" -sample_fmt s32");
                else if (depth == 2) sb.Append(" -sample_fmt s16");
                break;
            default:
                if (bitrate > 0) sb.Append($" -b:a {bitrate}k");
                break;
        }

        var sampleRate = p.GetInt(FormatParamIds.AudioSampleRate, 0);
        if (sampleRate > 0) sb.Append($" -ar {sampleRate}");
        var channels = p.GetInt(FormatParamIds.AudioChannels, 0);
        if (channels > 0) sb.Append($" -ac {channels}");

        sb.Append($" \"{output}\"");
        return sb.ToString();
    }

    /// <summary>WAV / AIFF 的位深与编码方式 → 实际编码器（ADPCM / 电话编码 / PCM）；其余容器的位深走独立参数。</summary>
    internal static string ResolveAudioCodec(FormatOption target, FormatParamValues p)
    {
        var depth = p.GetInt(FormatParamIds.AudioBitDepth, 0);
        if (target.Ext.Equals(".wav", StringComparison.OrdinalIgnoreCase))
        {
            switch (p.GetInt(FormatParamIds.AudioWavCodec, 0))
            {
                case 1: return "adpcm_ms";
                case 2: return "adpcm_ima_wav";
                case 3: return "pcm_alaw";
                case 4: return "pcm_mulaw";
            }
            return AudioBitDepths.Resolve(".wav", depth, target.DefaultACodec);
        }
        if (target.Ext.Equals(".aiff", StringComparison.OrdinalIgnoreCase))
            return AudioBitDepths.Resolve(".aiff", depth, target.DefaultACodec);
        return target.DefaultACodec;
    }

    private static string OpusApplicationName(int index) => index switch
    {
        1 => "voip",
        2 => "lowdelay",
        _ => "audio"
    };

    /// <summary>GIF 动图（filter_complex 内生成调色板 + 应用，单次完成，质量高且避免编码器崩溃）。</summary>
    private static string BuildGifArgs(string source, string output, FormatParamValues p)
    {
        var fps = Math.Clamp(p.GetInt(FormatParamIds.GifFps, 15), 1, 60);
        var width = p.GetInt(FormatParamIds.GifWidth, 480);
        var vfBase = width > 0
            ? $"fps={fps},scale={width}:-1:flags=lanczos"
            : $"fps={fps},scale=trunc(iw/2)*2:trunc(ih/2)*2";
        return $"-i \"{source}\" -filter_complex \"[0:v] {vfBase},split [a][b];[b] palettegen=stats_mode=diff [p];[a][p] paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle\" -an \"{output}\"";
    }

    private static string BuildVideoArgs(string source, string output, FormatOption target, FormatParamValues p)
    {
        var codec = FfmpegCodecs.VideoName(p.Get(FormatParamIds.VideoCodec, -1)) ?? target.DefaultVCodec;

        var sb = new System.Text.StringBuilder();
        sb.Append($"-i \"{source}\" -c:v {codec}");

        var rateMode = p.GetInt(FormatParamIds.VideoRateMode, 0);
        var bitrate = p.GetInt(FormatParamIds.VideoBitrate, 0);
        if (rateMode == 1 && bitrate > 0)
        {
            sb.Append($" -b:v {bitrate}k");
        }
        else
        {
            var crf = Math.Clamp(p.GetInt(FormatParamIds.VideoCrf, 23), 0, 63);
            var preset = Math.Clamp(p.GetInt(FormatParamIds.VideoPreset, 5), 0, FfmpegCodecs.X264Presets.Length - 1);
            switch (codec)
            {
                case "libx264":
                case "libx265":
                    sb.Append($" -crf {Math.Min(crf, 51)} -preset {FfmpegCodecs.X264Presets[preset]}");
                    break;
                case "libvpx-vp9":
                case "libvpx":
                    sb.Append($" -crf {crf} -b:v 0 -row-mt 1 -deadline good -cpu-used {FfmpegCodecs.SpeedForPreset(preset)}");
                    break;
                case "libaom-av1":
                    sb.Append($" -crf {crf} -b:v 0 -row-mt 1 -cpu-used {FfmpegCodecs.SpeedForPreset(preset)}");
                    break;
                case "mpeg4":
                case "wmv2":
                    sb.Append($" -q:v {QuantizerForCrf(crf)}");
                    break;
            }
        }

        sb.Append(" -pix_fmt yuv420p");
        if (codec == "libx265" && target.Ext is ".mp4" or ".mov")
            sb.Append(" -tag:v hvc1");

        // 最长边缩放（保持比例，force_original_aspect_ratio 只缩小；第二个 scale 保证偶数尺寸）
        var width = p.GetInt(FormatParamIds.VideoWidth, 0);
        if (width > 0)
            sb.Append($" -vf \"scale={width}:{width}:force_original_aspect_ratio=decrease,scale=trunc(iw/2)*2:trunc(ih/2)*2\"");
        var fps = p.GetInt(FormatParamIds.VideoFps, 0);
        if (fps > 0)
            sb.Append($" -r {Math.Clamp(fps, 1, 240)}");

        var audioChoice = p.GetInt(FormatParamIds.VideoAudioCodec, -2);
        if (audioChoice == (int)FfmpegCodecs.NoAudio)
        {
            sb.Append(" -an");
        }
        else
        {
            var audioCodec = FfmpegCodecs.AudioName(audioChoice) ?? target.DefaultACodec;
            if (audioCodec.Length > 0)
            {
                sb.Append($" -c:a {audioCodec}");
                var audioBitrate = p.GetInt(FormatParamIds.VideoAudioBitrate, 0);
                if (audioBitrate > 0) sb.Append($" -b:a {audioBitrate}k");
            }
            var sampleRate = p.GetInt(FormatParamIds.VideoSampleRate, 0);
            if (sampleRate > 0) sb.Append($" -ar {sampleRate}");
            var channels = p.GetInt(FormatParamIds.VideoChannels, 0);
            if (channels > 0) sb.Append($" -ac {channels}");
        }

        sb.Append($" \"{output}\"");
        return sb.ToString();
    }

    /// <summary>CRF 折算为 MPEG-4 / WMV2 的量化器（1 最好 - 31 最差）。</summary>
    internal static int QuantizerForCrf(int crf)
        => Math.Clamp((int)Math.Round(crf * 30.0 / 51.0) + 1, 1, 31);

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
    /// 构造 ImageMagick 参数：通用项（质量 / 去元数据 / 最长边缩放）+ 目标格式专属项
    /// （PNG 压缩级别与颜色类型、JPG 采样与渐进式、WebP 无损与方法、GIF 颜色数与抖动、TIFF 压缩方式…）。
    /// </summary>
    /// <param name="icoSizes">ICO 目标的多尺寸列表（icon:auto-resize），为空时默认 256,128,64,48,32,16。</param>
    public static string BuildMagickArgs(string source, FormatOption target, FormatParamValues? values = null,
        int[]? icoSizes = null)
    {
        var p = values ?? FormatParamValues.Empty;
        var output = BuildOutputPath(source, target.Ext);
        var sb = new System.Text.StringBuilder();
        sb.Append($"\"{source}\"");

        var quality = p.GetInt(FormatParamIds.ImageQuality, 0);
        if (quality > 0)
            sb.Append($" -quality {Math.Clamp(quality, 1, 100)}");

        switch (target.Ext)
        {
            case ".png":
                var level = p.GetInt(FormatParamIds.ImagePngLevel, -1);
                if (level >= 0)
                    sb.Append($" -define png:compression-level={Math.Clamp(level, 0, 9)}");
                var colorType = p.GetInt(FormatParamIds.ImagePngColorType, 0);
                if (colorType > 0)
                    sb.Append($" -define png:color-type={colorType}");
                break;
            case ".jpg":
            case ".jpeg":
                var sampling = p.GetInt(FormatParamIds.ImageSampling, 0);
                if (sampling > 0)
                    sb.Append($" -sampling-factor {SamplingFactors[Math.Clamp(sampling, 1, SamplingFactors.Length - 1)]}");
                if (p.GetFlag(FormatParamIds.ImageProgressive))
                    sb.Append(" -interlace Plane");
                break;
            case ".webp":
                if (p.GetFlag(FormatParamIds.ImageLossless))
                    sb.Append(" -define webp:lossless=true");
                var method = p.GetInt(FormatParamIds.ImageMethod, -1);
                if (method >= 0)
                    sb.Append($" -define webp:method={Math.Clamp(method, 0, 6)}");
                break;
            case ".avif":
                var speed = p.GetInt(FormatParamIds.ImageAvifSpeed, -1);
                if (speed >= 0)
                    sb.Append($" -define heic:speed={Math.Clamp(speed, 0, 9)}");
                break;
            case ".gif":
                if (p.Has(FormatParamIds.ImageGifDither))
                    sb.Append(p.GetFlag(FormatParamIds.ImageGifDither) ? " -dither Riemersma" : " -dither None");
                var colors = p.GetInt(FormatParamIds.ImageGifColors, 0);
                if (colors is > 0 and < 256)
                    sb.Append($" -colors {Math.Clamp(colors, 2, 256)}");
                break;
            case ".tiff":
                var compress = p.GetInt(FormatParamIds.ImageTiffCompress, 0);
                if (compress > 0)
                    sb.Append($" -compress {TiffCompressions[Math.Clamp(compress, 1, TiffCompressions.Length - 1)]}");
                break;
        }

        if (p.GetFlag(FormatParamIds.ImageStrip))
            sb.Append(" -strip");

        var maxDimension = p.GetInt(FormatParamIds.ImageMaxEdge, 0);
        if (maxDimension > 0 && target.Ext != ".ico")
            sb.Append($" -resize {maxDimension}x{maxDimension}>");

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

    private static readonly string[] SamplingFactors = { "", "4:4:4", "4:2:2", "4:2:0" };
    private static readonly string[] TiffCompressions = { "", "LZW", "Zip", "JPEG", "None" };

    /// <summary>
    /// 构造「图片 → MP4 / WebM 视频」的 FFmpeg 参数（编码器 / CRF / 时长 / 帧率 / 分辨率可调）。
    /// 动图（GIF）按原有帧序列转码；静态图片以 -loop 1 循环展示指定时长。
    /// </summary>
    public static string BuildImageVideoArgs(string source, FormatOption target, FormatParamValues? values = null)
    {
        var p = values ?? FormatParamValues.Empty;
        var output = BuildOutputPath(source, target.Ext);
        var duration = p.GetInt(FormatParamIds.VideoDuration, 5);
        if (duration <= 0) duration = 5;
        duration = Math.Clamp(duration, 1, 600);
        var maxEdge = p.GetInt(FormatParamIds.VideoWidth, 0);
        var fps = Math.Clamp(p.GetInt(FormatParamIds.VideoFps, 30), 1, 240);
        var crf = Math.Clamp(p.GetInt(FormatParamIds.VideoCrf, 23), 0, 63);
        var codec = FfmpegCodecs.VideoName(p.Get(FormatParamIds.VideoCodec, -1)) ?? target.DefaultVCodec;
        var isAnimated = Path.GetExtension(source).Equals(".gif", StringComparison.OrdinalIgnoreCase);

        var scale = maxEdge > 0
            ? $"scale={maxEdge}:{maxEdge}:force_original_aspect_ratio=decrease,scale=trunc(iw/2)*2:trunc(ih/2)*2"
            : "scale=trunc(iw/2)*2:trunc(ih/2)*2";

        var sb = new System.Text.StringBuilder();
        if (!isAnimated)
            sb.Append($"-loop 1 -t {duration} ");
        sb.Append($"-i \"{source}\" -c:v {codec} -pix_fmt yuv420p -vf \"{scale}\" -r {fps} -an");

        switch (codec)
        {
            case "libx264":
            case "libx265":
                if (!isAnimated)
                    sb.Append(" -tune stillimage");
                sb.Append($" -crf {Math.Min(crf, 51)}");
                if (codec == "libx265" && target.Ext == ".mp4")
                    sb.Append(" -tag:v hvc1");
                break;
            case "libvpx-vp9":
            case "libvpx":
            case "libaom-av1":
                sb.Append($" -b:v 0 -crf {crf}");
                break;
        }

        if (target.Ext == ".mp4")
            sb.Append(" -movflags +faststart");
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
        int quality, int maxDimension, bool strip)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var source in sources)
            sb.Append($"\"{source}\" ");

        if (quality > 0)
            sb.Append($"-quality {Math.Clamp(quality, 1, 100)}");
        if (strip)
            sb.Append(" -strip");
        if (maxDimension > 0)
            sb.Append($" -resize {maxDimension}x{maxDimension}>");

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