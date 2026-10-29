using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace TubaWinUi3.Services;

/// <summary>
/// OCR 文字识别（Windows OCR API / Windows.Media.Ocr，Win10+ 系统自带，
/// 识别在设备本地完成）。
/// 说明：Windows AI「文本识别」API（Microsoft.Windows.Vision.TextRecognizer，
/// 仅 Copilot+ PC）在当前 .NET 工具链中未提供投影（SDK 投影与 NuGet 均无该类型），
/// 因此按文档建议直接采用其回退方案 Windows OCR；后续 SDK 提供投影后可在
/// RecognizeImageFileAsync 中加入优先分支。
/// 解码失败的图片格式（如旧系统无 AVIF 编解码器）自动借助 ImageMagick 转 PNG 重试。
/// </summary>
public static class OcrService
{
    /// <summary>识别图片文件中的文字（整页文本，按行换行）。</summary>
    public static async Task<string> RecognizeImageFileAsync(string imagePath, CancellationToken ct = default)
    {
        var bitmap = await DecodeOrNullAsync(imagePath, maxDimension: 0, ct)
                     ?? await DecodeViaMagickAsync(imagePath, ct);
        if (bitmap is null)
            throw new InvalidOperationException(
                $"无法解码图片（可能缺少该格式的系统编解码器，可先下载 ImageMagick 引擎再试）：{Path.GetFileName(imagePath)}");

        try
        {
            // 图片尺寸超出引擎上限时等比缩小后重新解码
            var limit = OcrEngine.MaxImageDimension;
            if (limit > 0 && Math.Max(bitmap.PixelWidth, bitmap.PixelHeight) > limit)
            {
                bitmap.Dispose();
                bitmap = await DecodeOrNullAsync(imagePath, limit, ct)
                         ?? await DecodeViaMagickAsync(imagePath, ct)
                         ?? throw new InvalidOperationException("图片缩小后无法重新解码");
            }
            return await RecognizeAsync(bitmap, ct);
        }
        finally
        {
            bitmap.Dispose();
        }
    }

    /// <summary>系统是否具备 OCR 能力（用于界面状态展示）。</summary>
    public static bool IsEngineAvailable => OcrEngine.TryCreateFromUserProfileLanguages() is not null;

    private static async Task<string> RecognizeAsync(SoftwareBitmap bitmap, CancellationToken ct)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
            throw new InvalidOperationException(
                "系统未安装 OCR 语言包：请在 设置 → 时间和语言 → 语言和区域 中为中文/英文语言添加「基本输入」或完整语言包后重试");

        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
        {
            using var converted = SoftwareBitmap.Convert(bitmap, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
            return LinesToText(await engine.RecognizeAsync(converted).AsTask(ct));
        }
        return LinesToText(await engine.RecognizeAsync(bitmap).AsTask(ct));
    }

    private static string LinesToText(OcrResult result)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in result.Lines)
        {
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line.Text);
        }
        return sb.ToString();
    }

    /// <summary>解码图片为 Bgra8 SoftwareBitmap；maxDimension > 0 时等比缩小到上限内。失败返回 null。</summary>
    private static async Task<SoftwareBitmap?> DecodeOrNullAsync(string path, uint maxDimension, CancellationToken ct)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var stream = fs.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct);

            var transform = new BitmapTransform();
            uint width = decoder.PixelWidth, height = decoder.PixelHeight;
            if (maxDimension > 0 && Math.Max(width, height) > maxDimension && width > 0 && height > 0)
            {
                var scale = (double)maxDimension / Math.Max(width, height);
                transform.ScaledWidth = (uint)Math.Max(1, Math.Round(width * scale));
                transform.ScaledHeight = (uint)Math.Max(1, Math.Round(height * scale));
            }
            return await decoder.GetSoftwareBitmapAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage).AsTask(ct);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>借助 ImageMagick 把图片转成临时 PNG 再解码（应对系统缺编码器的格式）。</summary>
    private static async Task<SoftwareBitmap?> DecodeViaMagickAsync(string path, CancellationToken ct)
    {
        if (!MagickService.IsMagickReady) return null;
        var tmp = Path.Combine(Path.GetTempPath(), $"ocr_{Guid.NewGuid():N}.png");
        try
        {
            await MagickService.RunMagickAsync($"\"{path}\" \"{tmp}\"", ct);
            return await DecodeOrNullAsync(tmp, 0, ct);
        }
        catch
        {
            return null;
        }
        finally
        {
            try { File.Delete(tmp); } catch { }
        }
    }
}
