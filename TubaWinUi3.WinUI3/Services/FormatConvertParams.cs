namespace TubaWinUi3.Services;

/// <summary>格式参数的控件类型（页面据此自动生成控件）。</summary>
public enum FormatParamKind
{
    Slider,
    Number,
    Combo,
    Toggle
}

/// <summary>下拉选项：Label 展示，Value 写回参数值表（编码器类参数用下标）。</summary>
public sealed record FormatParamChoice(string Label, double Value);

/// <summary>
/// 目标格式的专属参数。页面按 Kind 生成「滑块 + 数值输入框」/ 下拉框 / 开关，
/// VisibleWhenId + VisibleWhenValue 支持联动显隐（如 VBR 质量仅在 VBR 模式下出现）。
/// </summary>
public sealed record FormatParam(
    string Id,
    string Label,
    FormatParamKind Kind,
    double Min = 0,
    double Max = 100,
    double Step = 1,
    double Default = 0,
    string Unit = "",
    string? Hint = null,
    IReadOnlyList<FormatParamChoice>? Choices = null,
    string? VisibleWhenId = null,
    double? VisibleWhenValue = null);

/// <summary>对话框读取到的参数值表（缺键 = 由转换器取默认值）。</summary>
public sealed class FormatParamValues
{
    public static readonly FormatParamValues Empty = new(new Dictionary<string, double>());

    private readonly IReadOnlyDictionary<string, double> _values;

    public FormatParamValues(IReadOnlyDictionary<string, double> values) => _values = values;

    public double Get(string id, double fallback)
        => _values.TryGetValue(id, out var v) && !double.IsNaN(v) ? v : fallback;

    public int GetInt(string id, int fallback)
        => (int)Math.Round(Get(id, fallback), MidpointRounding.AwayFromZero);

    public bool GetFlag(string id) => Get(id, 0) >= 0.5;

    public bool Has(string id) => _values.ContainsKey(id);
}

/// <summary>参数 id（页面与转换器共用，避免字符串散落）。</summary>
public static class FormatParamIds
{
    public const string AudioBitrate = "audio.bitrate";
    public const string AudioRateMode = "audio.rateMode";
    public const string AudioQuality = "audio.quality";
    public const string AudioSampleRate = "audio.sampleRate";
    public const string AudioChannels = "audio.channels";
    public const string AudioBitDepth = "audio.bitDepth";
    public const string AudioCompressionLevel = "audio.compressionLevel";
    public const string AudioOpusApplication = "audio.opusApplication";
    public const string AudioWavCodec = "audio.wavCodec";

    public const string VideoCodec = "video.codec";
    public const string VideoRateMode = "video.rateMode";
    public const string VideoCrf = "video.crf";
    public const string VideoBitrate = "video.bitrate";
    public const string VideoPreset = "video.preset";
    public const string VideoWidth = "video.width";
    public const string VideoFps = "video.fps";
    public const string VideoDuration = "video.duration";
    public const string VideoAudioCodec = "video.audioCodec";
    public const string VideoAudioBitrate = "video.audioBitrate";
    public const string VideoSampleRate = "video.sampleRate";
    public const string VideoChannels = "video.channels";

    public const string GifFps = "gif.fps";
    public const string GifWidth = "gif.width";

    public const string ImageQuality = "image.quality";
    public const string ImageMaxEdge = "image.maxEdge";
    public const string ImageStrip = "image.strip";
    public const string ImagePngLevel = "image.pngLevel";
    public const string ImagePngColorType = "image.pngColorType";
    public const string ImageProgressive = "image.progressive";
    public const string ImageSampling = "image.sampling";
    public const string ImageLossless = "image.lossless";
    public const string ImageMethod = "image.method";
    public const string ImageGifColors = "image.gifColors";
    public const string ImageGifDither = "image.gifDither";
    public const string ImageTiffCompress = "image.tiffCompress";
    public const string ImageAvifSpeed = "image.avifSpeed";
}

/// <summary>FFmpeg 编码器表，参数值即下标（NoAudio 例外，表示去掉音轨）。</summary>
public static class FfmpegCodecs
{
    public const double NoAudio = -1;

    public const double H264 = 0;
    public const double H265 = 1;
    public const double Vp9 = 2;
    public const double Vp8 = 3;
    public const double Av1 = 4;
    public const double Mpeg4 = 5;
    public const double Wmv2 = 6;

    public const double Aac = 0;
    public const double Mp3 = 1;
    public const double Ac3 = 2;
    public const double Opus = 3;
    public const double Vorbis = 4;
    public const double Flac = 5;
    public const double Wma = 6;

    public static readonly string[] VideoNames = { "libx264", "libx265", "libvpx-vp9", "libvpx", "libaom-av1", "mpeg4", "wmv2" };
    public static readonly string[] AudioNames = { "aac", "libmp3lame", "ac3", "libopus", "libvorbis", "flac", "wmav2" };

    public const string NoVideoCodec = "";

    public static readonly string[] X264Presets =
        { "ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow", "slower", "veryslow" };

    public static string? VideoName(double value) => At(VideoNames, value);
    public static string? AudioName(double value) => At(AudioNames, value);

    private static string? At(string[] table, double value)
    {
        var index = (int)Math.Round(value);
        return index >= 0 && index < table.Length ? table[index] : null;
    }

    /// <summary>编码预设下标 → libvpx / libaom 的 -cpu-used（0 最慢最好，8 最快）。</summary>
    public static int SpeedForPreset(int presetIndex) => presetIndex switch
    {
        <= 0 => 8,
        1 => 8,
        2 => 6,
        3 => 5,
        4 => 4,
        5 => 3,
        6 => 2,
        7 => 1,
        _ => 0
    };
}

/// <summary>PCM 位深表：下标 0 = 保持/默认，位深与编码器按容器对应。</summary>
public static class AudioBitDepths
{
    public static readonly string[] Labels = { "默认", "8 位", "16 位", "24 位", "32 位", "32 位浮点", "64 位浮点" };
    public static readonly int[] Bits = { 0, 8, 16, 24, 32, 32, 64 };

    public static readonly string[] Wav = { "", "pcm_u8", "pcm_s16le", "pcm_s24le", "pcm_s32le", "pcm_f32le", "pcm_f64le" };
    public static readonly string[] Aiff = { "", "pcm_s8", "pcm_s16be", "pcm_s24be", "pcm_s32be" };

    public static readonly FormatParamChoice[] WavChoices =
    {
        new("16 位整数（默认）", 2), new("8 位整数", 1), new("24 位整数", 3),
        new("32 位整数", 4), new("32 位浮点", 5), new("64 位浮点", 6)
    };

    public static readonly FormatParamChoice[] AiffChoices =
    {
        new("16 位整数（默认）", 2), new("8 位整数", 1), new("24 位整数", 3), new("32 位整数", 4)
    };

    public static readonly FormatParamChoice[] FlacChoices =
    {
        new("默认（跟随源）", 0), new("16 位整数", 2), new("24 位整数", 3)
    };

    public static int BitCount(int index) => index >= 0 && index < Bits.Length ? Bits[index] : 0;

    public static string Resolve(string containerExt, int index, string fallback)
    {
        var table = containerExt.Equals(".aiff", StringComparison.OrdinalIgnoreCase) ? Aiff : Wav;
        return index > 0 && index < table.Length && table[index].Length > 0 ? table[index] : fallback;
    }
}

/// <summary>各目标格式的参数集与取值表（页面 + 转换器共用）。</summary>
public static class FormatConvertParams
{
    public static readonly FormatParamChoice[] ChannelChoices =
    {
        new("保持原样", 0), new("单声道", 1), new("立体声", 2), new("5.1 环绕", 6)
    };

    public static readonly FormatParamChoice[] PresetChoices =
        FfmpegCodecs.X264Presets
            .Select((name, i) => new FormatParamChoice(i == 5 ? $"{name}（默认）" : name, i))
            .ToArray();

    public static FormatParamChoice[] RateChoices(params double[] values)
        => values.Select(v => new FormatParamChoice(v <= 0 ? "保持原样" : $"{v:0} Hz", v)).ToArray();

    public static readonly FormatParamChoice[] Mp3Rates = RateChoices(0, 8000, 11025, 16000, 22050, 24000, 32000, 44100, 48000);
    public static readonly FormatParamChoice[] AacRates = RateChoices(0, 8000, 11025, 16000, 22050, 32000, 44100, 48000, 64000, 88200, 96000);
    public static readonly FormatParamChoice[] VorbisRates = RateChoices(0, 8000, 11025, 16000, 22050, 32000, 44100, 48000, 96000, 192000);
    public static readonly FormatParamChoice[] WmaRates = RateChoices(0, 8000, 11025, 16000, 22050, 32000, 44100, 48000);
    public static readonly FormatParamChoice[] VideoAudioRates = RateChoices(0, 8000, 11025, 16000, 22050, 32000, 44100, 48000);
    public static readonly FormatParamChoice[] Ac3Rates = RateChoices(0, 32000, 44100, 48000);

    // ── 音频参数 ──

    public static FormatParam BitrateParam(double max, double def) => new(
        FormatParamIds.AudioBitrate, "码率", FormatParamKind.Slider, 0, max, 8, def, "kbps",
        "0 = 编码器默认；可拖动滑块，也可直接输入数值");

    public static FormatParam RateModeParam() => new(
        FormatParamIds.AudioRateMode, "码率模式", FormatParamKind.Combo,
        Choices: new FormatParamChoice[]
        {
            new("固定码率（CBR，兼容性最好）", 0),
            new("可变质量（VBR，同体积质量更好）", 1)
        });

    public static FormatParam VbrQualityParam(double def = 2) => new(
        FormatParamIds.AudioQuality, "VBR 质量", FormatParamKind.Slider, 0, 9, 1, def, "",
        "0 = 最好（体积最大），9 = 最差（体积最小）",
        VisibleWhenId: FormatParamIds.AudioRateMode, VisibleWhenValue: 1);

    public static FormatParam QualityParam(double min, double max, double def, string hint) => new(
        FormatParamIds.AudioQuality, "质量", FormatParamKind.Slider, min, max, 1, def, "", hint);

    public static FormatParam SampleRateParam(FormatParamChoice[] choices) => new(
        FormatParamIds.AudioSampleRate, "采样率", FormatParamKind.Combo, Choices: choices);

    public static FormatParam SampleRateSliderParam() => new(
        FormatParamIds.AudioSampleRate, "采样率", FormatParamKind.Slider, 0, 192000, 100, 0, "Hz",
        "0 = 保持原样；可拖动滑块，也可直接输入任意采样率（如 44100）");

    public static FormatParam ChannelsParam() => new(
        FormatParamIds.AudioChannels, "声道", FormatParamKind.Combo, Choices: ChannelChoices);

    public static FormatParam BitDepthParam(FormatParamChoice[] choices, string hint) => new(
        FormatParamIds.AudioBitDepth, "位深", FormatParamKind.Combo, Choices: choices, Hint: hint,
        Default: choices.Length > 0 ? choices[0].Value : 0);

    public static string[] WavCodecLabels =
    {
        "PCM 无损（按位深）", "ADPCM Microsoft（约 1/4 体积）", "ADPCM IMA（约 1/4 体积）",
        "A-law（8 位，电话音质）", "μ-law（8 位，电话音质）"
    };

    public static FormatParam WavCodecParam => new(
        FormatParamIds.AudioWavCodec, "音频编码", FormatParamKind.Combo,
        Choices: WavCodecLabels.Select((label, i) => new FormatParamChoice(label, i)).ToArray(),
        Hint: "ADPCM / 电话编码体积更小但不是无损（选择后位深无效）");

    public static readonly FormatParam[] Mp3Params =
    {
        RateModeParam(),
        BitrateParam(320, 192) with { VisibleWhenId = FormatParamIds.AudioRateMode, VisibleWhenValue = 0 },
        VbrQualityParam(), SampleRateParam(Mp3Rates), ChannelsParam()
    };

    public static readonly FormatParam[] AacParams =
    {
        BitrateParam(512, 192), SampleRateParam(AacRates), ChannelsParam()
    };

    public static readonly FormatParam[] FlacParams =
    {
        new FormatParam(FormatParamIds.AudioCompressionLevel, "压缩级别", FormatParamKind.Slider, 0, 12, 1, 5, "",
            "越大体积越小、编码越慢"),
        BitDepthParam(AudioBitDepths.FlacChoices, "默认跟随源文件；16/24 位可强制降位"),
        SampleRateParam(VorbisRates), ChannelsParam()
    };

    public static readonly FormatParam[] WavParams =
    {
        WavCodecParam,
        new FormatParam(FormatParamIds.AudioBitDepth, "位深", FormatParamKind.Combo, Default: 2,
            Hint: "决定无损 WAV 的码率：码率 ≈ 采样率 × 位深 × 声道",
            Choices: AudioBitDepths.WavChoices,
            VisibleWhenId: FormatParamIds.AudioWavCodec, VisibleWhenValue: 0),
        SampleRateSliderParam(), ChannelsParam()
    };

    public static readonly FormatParam[] AiffParams =
    {
        BitDepthParam(AudioBitDepths.AiffChoices, "码率 ≈ 采样率 × 位深 × 声道"),
        SampleRateSliderParam(), ChannelsParam()
    };

    public static readonly FormatParam[] OggParams =
    {
        QualityParam(0, 10, 5, "0 = 体积最小，10 = 质量最好（Vorbis 为可变质量编码）"),
        SampleRateParam(VorbisRates), ChannelsParam()
    };

    public static FormatParam OpusApplicationParam => new(
        FormatParamIds.AudioOpusApplication, "编码用途", FormatParamKind.Combo,
        Choices: new FormatParamChoice[]
        {
            new("音频（音乐，默认）", 0), new("语音（对讲，更省码率）", 1), new("低延迟（实时传输）", 2)
        });

    public static readonly FormatParam[] OpusParams =
    {
        BitrateParam(510, 128), OpusApplicationParam, ChannelsParam()
    };

    public static readonly FormatParam[] WmaParams =
    {
        BitrateParam(192, 128), SampleRateParam(WmaRates), ChannelsParam()
    };

    // ── 视频参数 ──

    public static readonly FormatParamChoice[] RateModeChoices =
    {
        new("质量优先（CRF，推荐）", 0), new("指定码率", 1)
    };

    public static readonly FormatParamChoice[] Mp4VideoCodecs =
    {
        new("H.264 / AVC（兼容性最好）", FfmpegCodecs.H264),
        new("H.265 / HEVC（同画质更小）", FfmpegCodecs.H265),
        new("AV1（体积最小，编码慢）", FfmpegCodecs.Av1)
    };

    public static readonly FormatParamChoice[] MkvVideoCodecs =
    {
        new("H.264 / AVC（兼容性最好）", FfmpegCodecs.H264),
        new("H.265 / HEVC（同画质更小）", FfmpegCodecs.H265),
        new("VP9（开源编码）", FfmpegCodecs.Vp9),
        new("AV1（体积最小，编码慢）", FfmpegCodecs.Av1),
        new("MPEG-4（老设备）", FfmpegCodecs.Mpeg4)
    };

    public static readonly FormatParamChoice[] WebmVideoCodecs =
    {
        new("VP9（默认）", FfmpegCodecs.Vp9),
        new("VP8（老设备）", FfmpegCodecs.Vp8),
        new("AV1（体积最小，编码慢）", FfmpegCodecs.Av1)
    };

    public static readonly FormatParamChoice[] AviVideoCodecs =
    {
        new("H.264 / AVC（兼容性最好）", FfmpegCodecs.H264),
        new("MPEG-4（老设备）", FfmpegCodecs.Mpeg4)
    };

    public static readonly FormatParamChoice[] TsVideoCodecs =
    {
        new("H.264 / AVC（默认）", FfmpegCodecs.H264),
        new("H.265 / HEVC", FfmpegCodecs.H265)
    };

    public static readonly FormatParamChoice[] H264OnlyCodecs =
    {
        new("H.264 / AVC", FfmpegCodecs.H264)
    };

    public static readonly FormatParamChoice[] Wmv2OnlyCodecs =
    {
        new("WMV2（Windows Media 视频）", FfmpegCodecs.Wmv2)
    };

    public static readonly FormatParamChoice[] ImageToMp4Codecs =
    {
        new("H.264 / AVC（兼容性最好）", FfmpegCodecs.H264),
        new("H.265 / HEVC（同画质更小）", FfmpegCodecs.H265)
    };

    public static readonly FormatParamChoice[] ImageToWebmCodecs =
    {
        new("VP9（默认）", FfmpegCodecs.Vp9),
        new("VP8（老设备）", FfmpegCodecs.Vp8),
        new("AV1（体积最小，编码慢）", FfmpegCodecs.Av1)
    };

    public static readonly FormatParamChoice[] WmaOnlyCodecs =
    {
        new("WMA（Windows Media 音频）", FfmpegCodecs.Wma)
    };

    public static readonly FormatParamChoice[] Mp4AudioCodecs =
    {
        new("AAC（默认）", FfmpegCodecs.Aac), new("MP3", FfmpegCodecs.Mp3),
        new("AC-3（环绕声）", FfmpegCodecs.Ac3), new("去掉音轨", FfmpegCodecs.NoAudio)
    };

    public static readonly FormatParamChoice[] MkvAudioCodecs =
    {
        new("AAC（默认）", FfmpegCodecs.Aac), new("MP3", FfmpegCodecs.Mp3), new("AC-3（环绕声）", FfmpegCodecs.Ac3),
        new("Opus（高压缩）", FfmpegCodecs.Opus), new("Vorbis", FfmpegCodecs.Vorbis),
        new("FLAC（无损）", FfmpegCodecs.Flac), new("去掉音轨", FfmpegCodecs.NoAudio)
    };

    public static readonly FormatParamChoice[] WebmAudioCodecs =
    {
        new("Opus（默认，高压缩）", FfmpegCodecs.Opus), new("Vorbis", FfmpegCodecs.Vorbis),
        new("去掉音轨", FfmpegCodecs.NoAudio)
    };

    public static readonly FormatParamChoice[] AviAudioCodecs =
    {
        new("MP3（默认）", FfmpegCodecs.Mp3), new("AC-3（环绕声）", FfmpegCodecs.Ac3),
        new("去掉音轨", FfmpegCodecs.NoAudio)
    };

    public static readonly FormatParamChoice[] TsAudioCodecs =
    {
        new("AAC（默认）", FfmpegCodecs.Aac), new("MP3", FfmpegCodecs.Mp3),
        new("AC-3（环绕声）", FfmpegCodecs.Ac3), new("去掉音轨", FfmpegCodecs.NoAudio)
    };

    public static readonly FormatParamChoice[] FlvAudioCodecs =
    {
        new("AAC（默认）", FfmpegCodecs.Aac), new("MP3", FfmpegCodecs.Mp3), new("去掉音轨", FfmpegCodecs.NoAudio)
    };

    public static FormatParam[] VideoParams(double crfMax, double crfDefault,
        FormatParamChoice[] videoCodecs, FormatParamChoice[] audioCodecs)
    {
        return new FormatParam[]
        {
            new(FormatParamIds.VideoCodec, "视频编码器", FormatParamKind.Combo, Default: videoCodecs[0].Value,
                Choices: videoCodecs),
            new(FormatParamIds.VideoRateMode, "码率控制", FormatParamKind.Combo, Choices: RateModeChoices),
            new(FormatParamIds.VideoCrf, "画质 CRF", FormatParamKind.Slider, 0, crfMax, 1, crfDefault, "",
                $"数值越小画质越高、体积越大（{(crfMax > 51 ? "VP9 / AV1：0-63" : "H.264 / H.265：0-51")}）；MPEG-4 / WMV2 会折算为量化器",
                VisibleWhenId: FormatParamIds.VideoRateMode, VisibleWhenValue: 0),
            new(FormatParamIds.VideoBitrate, "视频码率", FormatParamKind.Slider, 0, 60000, 100, 0, "kbps",
                "0 = 编码器默认；可与 CRF 二选一",
                VisibleWhenId: FormatParamIds.VideoRateMode, VisibleWhenValue: 1),
            new(FormatParamIds.VideoPreset, "编码预设", FormatParamKind.Combo, Default: 5, Choices: PresetChoices,
                Hint: "越慢同画质体积越小（VP9 / AV1 折算为速度档）"),
            new(FormatParamIds.VideoWidth, "分辨率（最长边）", FormatParamKind.Slider, 0, 7680, 10, 0, "px",
                "0 = 不缩放，保持原分辨率"),
            new(FormatParamIds.VideoFps, "帧率", FormatParamKind.Slider, 0, 120, 1, 0, "fps", "0 = 保持原帧率"),
            new(FormatParamIds.VideoAudioCodec, "音频编码器", FormatParamKind.Combo, Default: audioCodecs[0].Value,
                Choices: audioCodecs),
            new(FormatParamIds.VideoAudioBitrate, "音频码率", FormatParamKind.Slider, 0, 512, 8, 192, "kbps",
                "0 = 编码器默认"),
            new(FormatParamIds.VideoSampleRate, "音频采样率", FormatParamKind.Combo, Choices: VideoAudioRates),
            new(FormatParamIds.VideoChannels, "音频声道", FormatParamKind.Combo, Choices: ChannelChoices)
        };
    }

    public static readonly FormatParam[] GifVideoParams =
    {
        new(FormatParamIds.GifFps, "帧率", FormatParamKind.Slider, 5, 30, 1, 15, "fps", "越高越流畅、体积越大"),
        new(FormatParamIds.GifWidth, "宽度", FormatParamKind.Slider, 0, 1920, 10, 480, "px", "0 = 保持原始宽度")
    };

    public static FormatParam[] ImageVideoParams(double crfDefault, FormatParamChoice[] codecs) => new FormatParam[]
    {
        new(FormatParamIds.VideoCodec, "视频编码器", FormatParamKind.Combo, Default: codecs[0].Value, Choices: codecs),
        new(FormatParamIds.VideoCrf, "画质 CRF", FormatParamKind.Slider, 0, 51, 1, crfDefault, "",
            "数值越小画质越高、体积越大"),
        new(FormatParamIds.VideoDuration, "视频时长", FormatParamKind.Slider, 1, 600, 1, 5, "秒",
            "静态图片循环展示的时长；GIF 动图按原帧率"),
        new(FormatParamIds.VideoFps, "帧率", FormatParamKind.Slider, 1, 60, 1, 30, "fps", "越高越流畅、体积越大"),
        new(FormatParamIds.VideoWidth, "分辨率（最长边）", FormatParamKind.Slider, 0, 7680, 10, 0, "px",
            "0 = 原尺寸")
    };

    // ── 图片参数 ──

    public static FormatParam ImageQualityParam(double def, string hint) => new(
        FormatParamIds.ImageQuality, "质量", FormatParamKind.Slider, 0, 100, 1, def, "",
        hint);

    public static readonly FormatParam ImageMaxEdgeParam = new(
        FormatParamIds.ImageMaxEdge, "最长边", FormatParamKind.Slider, 0, 20000, 10, 0, "px",
        "0 = 不缩放；只缩小不放大");

    public static readonly FormatParam ImageStripParam = new(
        FormatParamIds.ImageStrip, "去除元数据", FormatParamKind.Toggle,
        Hint: "删除 EXIF / ICC 等信息，体积更小");

    public static readonly FormatParam[] JpegParams =
    {
        ImageQualityParam(85, "0 = 引擎默认；85 左右兼顾画质与体积"),
        new(FormatParamIds.ImageSampling, "色度采样", FormatParamKind.Combo,
            Choices: new FormatParamChoice[]
            {
                new("自动", 0), new("4:4:4（最清晰）", 1), new("4:2:2", 2), new("4:2:0（体积最小）", 3)
            },
            Hint: "4:4:4 保留彩色细节，适合截图 / 文字图"),
        new(FormatParamIds.ImageProgressive, "渐进式", FormatParamKind.Toggle,
            Hint: "网页加载时逐层显示，体积略小"),
        ImageMaxEdgeParam, ImageStripParam
    };

    public static readonly FormatParam[] PngParams =
    {
        new(FormatParamIds.ImagePngLevel, "压缩级别", FormatParamKind.Slider, 0, 9, 1, 7, "",
            "0 = 最快，9 = 体积最小（无损，只影响耗时）"),
        new(FormatParamIds.ImagePngColorType, "颜色类型", FormatParamKind.Combo,
            Choices: new FormatParamChoice[]
            {
                new("自动（保持原样）", 0), new("真彩 RGB", 2), new("真彩 + 透明 RGBA", 6), new("256 色索引（体积最小）", 3)
            },
            Hint: "索引色适合图标 / 截图，会损失渐变"),
        ImageMaxEdgeParam, ImageStripParam
    };

    public static readonly FormatParam[] WebpParams =
    {
        ImageQualityParam(85, "0 = 引擎默认；无损模式下忽略质量"),
        new(FormatParamIds.ImageLossless, "无损压缩", FormatParamKind.Toggle,
            Hint: "体积明显变大，画质完全无损"),
        new(FormatParamIds.ImageMethod, "压缩方法", FormatParamKind.Slider, 0, 6, 1, 4, "",
            "0 = 最快，6 = 压缩率最高（更慢）"),
        ImageMaxEdgeParam, ImageStripParam
    };

    public static readonly FormatParam[] AvifParams =
    {
        ImageQualityParam(60, "0 = 引擎默认；AVIF 用较低质量即可获得好观感"),
        new(FormatParamIds.ImageAvifSpeed, "编码速度", FormatParamKind.Slider, 0, 9, 1, 6, "",
            "0 = 最慢最小，9 = 最快最大"),
        ImageMaxEdgeParam, ImageStripParam
    };

    public static readonly FormatParam[] HeicParams =
    {
        ImageQualityParam(80, "0 = 引擎默认"),
        ImageMaxEdgeParam, ImageStripParam
    };

    public static readonly FormatParam[] GifImageParams =
    {
        new(FormatParamIds.ImageGifColors, "颜色数", FormatParamKind.Slider, 2, 256, 1, 256, "",
            "越少体积越小；256 = 不限制"),
        new(FormatParamIds.ImageGifDither, "抖动", FormatParamKind.Toggle, Default: 1,
            Hint: "渐变更平滑，但体积更大"),
        ImageMaxEdgeParam, ImageStripParam
    };

    public static readonly FormatParam[] TiffParams =
    {
        new(FormatParamIds.ImageTiffCompress, "压缩方式", FormatParamKind.Combo,
            Choices: new FormatParamChoice[]
            {
                new("自动", 0), new("LZW（无损）", 1), new("ZIP（无损，更小）", 2),
                new("JPEG（有损，最小）", 3), new("不压缩", 4)
            }),
        ImageMaxEdgeParam, ImageStripParam
    };

    public static readonly FormatParam[] PlainImageParams = { ImageMaxEdgeParam, ImageStripParam };

    public static readonly FormatParam[] ImagePdfParams =
    {
        ImageQualityParam(90, "PDF 内嵌图片的 JPEG 质量"),
        ImageMaxEdgeParam, ImageStripParam
    };

    /// <summary>WAV / AIFF 的等效码率估算（位深 × 采样率 × 声道）。</summary>
    public static double EstimatePcmBitrateKbps(int bitsPerSample, int sampleRate, int channels)
        => bitsPerSample * (double)sampleRate * channels / 1000.0;

    public static int EffectiveBits(FormatOption target, FormatParamValues values)
    {
        var isWav = target.Ext.Equals(".wav", StringComparison.OrdinalIgnoreCase);
        var depth = values.GetInt(FormatParamIds.AudioBitDepth, isWav ? 2 : 2);
        var bits = AudioBitDepths.BitCount(depth);
        if (bits == 0) bits = 16;

        if (isWav)
        {
            bits = values.GetInt(FormatParamIds.AudioWavCodec, 0) switch
            {
                1 or 2 => 4,
                3 or 4 => 8,
                _ => bits
            };
        }
        return bits;
    }

    /// <summary>参数汇总（「已自定义：…」+ WAV/AIFF 等效码率估算），无参数时返回空串。</summary>
    public static string Summarize(FormatOption target, FormatParamValues values)
    {
        var parameters = target.ParamList;
        if (parameters.Count == 0) return "";

        var parts = new List<string>();
        foreach (var p in parameters)
        {
            if (!IsVisible(p, parameters, values)) continue;

            if (p.Kind == FormatParamKind.Toggle)
            {
                if (values.GetFlag(p.Id)) parts.Add(p.Label);
                continue;
            }

            var v = values.Get(p.Id, p.Default);
            if (Math.Abs(v - p.Default) < 0.0001) continue;

            parts.Add(p.Kind == FormatParamKind.Combo
                ? $"{ShortLabel(p)}：{ChoiceLabel(p, v)}"
                : $"{ShortLabel(p)} {v:0.###}{(p.Unit.Length > 0 ? " " + p.Unit : "")}");
        }

        var text = parts.Count > 0 ? "已自定义：" + string.Join(" · ", parts) : "使用默认参数";
        var estimate = BitrateEstimate(target, values);
        return estimate is null ? text : $"{text}（{estimate}）";
    }

    /// <summary>按 VisibleWhen 规则判断参数当前是否可见（与对话框的显隐逻辑一致）。</summary>
    public static bool IsVisible(FormatParam param, IReadOnlyList<FormatParam> all, FormatParamValues values)
    {
        if (param.VisibleWhenId is null) return true;
        var driver = all.FirstOrDefault(p => p.Id == param.VisibleWhenId);
        if (driver is null) return true;
        return Math.Abs(values.Get(driver.Id, driver.Default) - (param.VisibleWhenValue ?? 0)) < 0.001;
    }

    public static string ChoiceLabel(FormatParam p, double value)
        => p.Choices?.FirstOrDefault(c => Math.Abs(c.Value - value) < 0.0001)?.Label ?? $"{value:0.###}";

    private static string ShortLabel(FormatParam p)
    {
        var index = p.Label.IndexOf('（');
        return index > 0 ? p.Label[..index] : p.Label;
    }

    /// <summary>无损音频的等效码率提示；采样率或声道为「保持原样」时无法估算，返回 null。</summary>
    public static string? BitrateEstimate(FormatOption target, FormatParamValues values)
    {
        if (!target.Ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            && !target.Ext.Equals(".aiff", StringComparison.OrdinalIgnoreCase))
            return null;

        var rate = values.GetInt(FormatParamIds.AudioSampleRate, 0);
        var channels = values.GetInt(FormatParamIds.AudioChannels, 0);
        if (rate <= 0 || channels <= 0) return null;

        var bits = EffectiveBits(target, values);
        return $"等效码率约 {EstimatePcmBitrateKbps(bits, rate, channels):0} kbps";
    }
}
