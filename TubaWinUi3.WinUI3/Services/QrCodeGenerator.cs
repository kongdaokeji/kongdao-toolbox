using System.Drawing;
using System.Drawing.Imaging;

namespace TubaWinUi3.Services;

public static class QrCodeGenerator
{
    public static byte[] GeneratePng(string text, int size = 200)
    {
        using var qrGenerator = new QRCoder.QRCodeGenerator();
        var qrCodeData = qrGenerator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.M);
        using var qrCode = new QRCoder.QRCode(qrCodeData);
        using var bmp = qrCode.GetGraphic(size / 25, Color.Black, Color.White, true);
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return ms.ToArray();
    }
}
