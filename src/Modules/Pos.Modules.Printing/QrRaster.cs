using QRCoder;

namespace Pos.Modules.Printing;

/// <summary>
/// v1.1: QR dibujado como imagen de 1 bit, para impresoras que no tienen el comando de QR (GS ( k),
/// como muchas de 58 mm. Corrección M y módulos de 6 puntos (o menos si no cabe), como el QR nativo.
/// </summary>
public static class QrRaster
{
    private const int ModuleDots = 6;
    private const int QuietZoneModules = 2;

    public static MonochromeImage Render(string data, int maxWidthDots)
    {
        using var generator = new QRCodeGenerator();
        using var code = generator.CreateQrCode(data, QRCodeGenerator.ECCLevel.M, forceUtf8: true);
        var matrix = code.ModuleMatrix; // incluye una zona blanca de 4 módulos alrededor
        var border = 4 - QuietZoneModules;
        var modules = matrix.Count - 2 * border;
        var dots = Math.Max(1, Math.Min(ModuleDots, maxWidthDots / modules));
        var size = modules * dots;

        var bytesPerRow = (size + 7) / 8;
        var bits = new byte[bytesPerRow * size];
        for (var y = 0; y < size; y++)
        {
            var row = matrix[border + y / dots];
            for (var x = 0; x < size; x++)
            {
                if (row[border + x / dots])
                    bits[y * bytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
            }
        }
        return new MonochromeImage(bytesPerRow, size, bits);
    }
}
