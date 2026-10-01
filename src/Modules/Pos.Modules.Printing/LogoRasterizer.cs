using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace Pos.Modules.Printing;

/// <summary>Imagen de 1 bit por punto, fila a fila; un bit a 1 es un punto negro.</summary>
public sealed record MonochromeImage(int BytesPerRow, int Height, byte[] Data);

/// <summary>Convierte el logo del negocio (IMP-03) a blanco y negro para la impresora térmica.</summary>
public static class LogoRasterizer
{
    private const int MaxHeight = 240;

    public static bool TryRasterize(string path, int maxWidthDots, out MonochromeImage image)
    {
        image = new MonochromeImage(0, 0, []);
        try
        {
            using var source = new Bitmap(path);
            var scale = Math.Min(1.0, Math.Min((double)maxWidthDots / source.Width, (double)MaxHeight / source.Height));
            var width = Math.Max(8, (int)(source.Width * scale));
            var height = Math.Max(1, (int)(source.Height * scale));

            using var scaled = new Bitmap(width, height);
            using (var g = Graphics.FromImage(scaled))
            {
                g.Clear(Color.White); // lo transparente se imprime en blanco
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(source, 0, 0, width, height);
            }

            var bytesPerRow = (width + 7) / 8;
            var data = new byte[bytesPerRow * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var c = scaled.GetPixel(x, y);
                    var luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
                    if (luminance < 128)
                        data[y * bytesPerRow + x / 8] |= (byte)(0x80 >> (x % 8));
                }
            }

            image = new MonochromeImage(bytesPerRow, height, data);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or IOException or OutOfMemoryException or ExternalException)
        {
            return false; // logo dañado o formato no soportado: el ticket sale sin logo
        }
    }
}
