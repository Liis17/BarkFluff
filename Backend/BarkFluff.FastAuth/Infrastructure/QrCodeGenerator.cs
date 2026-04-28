using QRCoder;

namespace BarkFluff.FastAuth.Infrastructure;

public class QrCodeGenerator
{
    /// <summary>
    /// Возвращает PNG QR-кода в base64 для значения payload.
    /// </summary>
    public string GeneratePngBase64(string payload)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrCodeData = qrGenerator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        var pngQrCode = new PngByteQRCode(qrCodeData);
        var bytes = pngQrCode.GetGraphic(20);
        return Convert.ToBase64String(bytes);
    }
}
