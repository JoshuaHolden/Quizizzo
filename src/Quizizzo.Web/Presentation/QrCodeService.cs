using QRCoder;

namespace Quizizzo.Web.Presentation;

public sealed class QrCodeService
{
    public string CreatePngDataUri(string payload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(payload);
        using var data = QRCodeGenerator.GenerateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(data);
        var bytes = qrCode.GetGraphic(12);
        return $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
    }
}
