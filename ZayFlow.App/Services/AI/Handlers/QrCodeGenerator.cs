using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using QRCoder;

namespace ZayFlow.App.Services.AI.Handlers;

/// <summary>
/// QR Code generator using QRCoder library — produces real scannable QR codes.
/// </summary>
public static class QrCodeGenerator
{
    public static string GenerateAndSave(string text, string savePath, int scale = 12)
    {
        using var qrGenerator = new QRCoder.QRCodeGenerator();
        var qrData = qrGenerator.CreateQrCode(text, QRCoder.QRCodeGenerator.ECCLevel.L);
        using var qrCode = new BitmapByteQRCode(qrData);
        var pngBytes = qrCode.GetGraphic(scale);

        Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);
        File.WriteAllBytes(savePath, pngBytes);
        return savePath;
    }
}
