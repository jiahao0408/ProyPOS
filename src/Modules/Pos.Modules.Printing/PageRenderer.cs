using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using Pos.Data;
using ZXing;
using ZXing.Common;
using ZXing.OneD;

namespace Pos.Modules.Printing;

/// <summary>
/// v2: dibuja un ticket (o un cierre Z, o etiquetas) como imágenes para una impresora normal de Windows.
/// Mismo contenido que en la térmica: el texto con letra de ancho fijo y los mismos caracteres por línea,
/// y el QR, el código de barras y el logo como imagen. Cada corte (<see cref="ReceiptCut"/>) empieza una
/// página nueva: en una impresora de etiquetas, cada etiqueta sale en la suya. El pulso del cajón no se dibuja.
/// </summary>
public static class PageRenderer
{
    /// <summary>Resolución de las imágenes: suficiente para láser y tinta, y QR y códigos de barras nítidos.</summary>
    public const int Dpi = 300;

    private const string FontFamily = "Consolas";

    /// <summary>Una imagen PNG por página (por ticket o por etiqueta).</summary>
    public static IReadOnlyList<byte[]> Render(IReadOnlyList<ReceiptElement> receipt, PrinterProfile printer)
    {
        var pages = new List<List<ReceiptElement>> { new() };
        foreach (var element in receipt)
        {
            if (element is ReceiptCut)
                pages.Add([]);
            else if (element is not ReceiptDrawerKick)
                pages[^1].Add(element);
        }
        return pages.Where(p => p.Count > 0).Select(p => RenderPage(p, printer)).ToList();
    }

    /// <summary>Ancho de la imagen en píxeles: el ancho imprimible del ticket (48 o 72 mm) a 300 ppp.</summary>
    public static int WidthPixels(PrinterProfile printer) => (int)Math.Round(printer.PrintableWidthMm / 25.4 * Dpi);

    private static byte[] RenderPage(IReadOnlyList<ReceiptElement> elements, PrinterProfile printer)
    {
        var width = WidthPixels(printer);
        using var measure = new Bitmap(1, 1);
        using var measureGraphics = Graphics.FromImage(measure);
        var normal = FontFor(measureGraphics, printer.CharsPerLine, width);
        using var large = new Font(normal.FontFamily, normal.Size * 2, FontStyle.Bold, GraphicsUnit.Pixel);
        using var bold = new Font(normal, FontStyle.Bold);
        var lineHeight = (int)Math.Ceiling(normal.GetHeight(measureGraphics) * 1.1);
        var scale = Dpi / 203.0; // los tamaños de la térmica están en puntos de 203 ppp

        // Primero se calcula la altura de cada elemento; luego se dibuja.
        var heights = elements.Select(e => e switch
        {
            ReceiptText t => t.Large ? lineHeight * 2 : lineHeight,
            ReceiptSeparator or ReceiptBlankLine => lineHeight,
            ReceiptQr qr => QrRaster.Render(qr.Data, width, QrModulePixels).Height + lineHeight / 2,
            ReceiptBarcode => (int)(80 * scale) + lineHeight + lineHeight / 2,
            ReceiptLogo logo => LogoSize(logo.Path, width, scale).Height + lineHeight / 2,
            _ => 0,
        }).ToList();
        var height = Math.Max(lineHeight, heights.Sum() + lineHeight);

        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        bitmap.SetResolution(Dpi, Dpi);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            var y = 0;
            for (var i = 0; i < elements.Count; i++)
            {
                switch (elements[i])
                {
                    case ReceiptText t:
                        DrawText(g, t.Text, t.Large ? large : t.Bold ? bold : normal, t.Align, width, y,
                            t.Large ? printer.CharsPerLine / 2 : printer.CharsPerLine);
                        break;
                    case ReceiptSeparator:
                        using (var pen = new Pen(Color.Black, Math.Max(1, Dpi / 150f)) { DashStyle = DashStyle.Dash })
                            g.DrawLine(pen, 0, y + lineHeight / 2, width, y + lineHeight / 2);
                        break;
                    case ReceiptQr qr:
                        DrawMonochrome(g, QrRaster.Render(qr.Data, width, QrModulePixels), 1, width, y);
                        break;
                    case ReceiptBarcode barcode:
                        DrawBarcode(g, barcode.Code, normal, width, y, (int)(80 * scale));
                        break;
                    case ReceiptLogo logo:
                        DrawLogo(g, logo.Path, width, y, scale);
                        break;
                }
                y += heights[i];
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    /// <summary>Letra de ancho fijo del tamaño justo para que quepan los caracteres por línea del ticket.</summary>
    private static Font FontFor(Graphics g, int charsPerLine, int width)
    {
        using var probe = new Font(FontFamily, 100, FontStyle.Regular, GraphicsUnit.Pixel);
        var charWidth = g.MeasureString(new string('M', 20), probe, PointF.Empty, StringFormat.GenericTypographic).Width / 20;
        return new Font(FontFamily, 100f * width / (charWidth * charsPerLine) * 0.98f, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    /// <summary>Una línea más larga que el ticket (la térmica la partiría) se dibuja con letra más pequeña para que quepa.</summary>
    private static void DrawText(Graphics g, string text, Font font, ReceiptAlign align, int width, int y, int charsPerLine = int.MaxValue)
    {
        if (text.Length > charsPerLine)
        {
            using var smaller = new Font(font.FontFamily, font.Size * charsPerLine / text.Length, font.Style, GraphicsUnit.Pixel);
            DrawText(g, text, smaller, align, width, y);
            return;
        }
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = align switch
            {
                ReceiptAlign.Center => StringAlignment.Center,
                ReceiptAlign.Right => StringAlignment.Far,
                _ => StringAlignment.Near,
            },
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces,
            Trimming = StringTrimming.None,
        };
        g.DrawString(text, font, Brushes.Black, new RectangleF(0, y, width, font.GetHeight(g) * 1.2f), format);
    }

    /// <summary>Módulos del QR de unos 0,75 mm, como en la térmica (6 puntos a 203 ppp).</summary>
    private const int QrModulePixels = 9;

    private static void DrawMonochrome(Graphics g, MonochromeImage image, int factor, int width, int y)
    {
        var pixels = image.BytesPerRow * 8;
        var x0 = Math.Max(0, (width - pixels * factor) / 2);
        for (var row = 0; row < image.Height; row++)
        {
            for (var col = 0; col < pixels; col++)
            {
                if ((image.Data[row * image.BytesPerRow + col / 8] & (0x80 >> (col % 8))) != 0)
                    g.FillRectangle(Brushes.Black, x0 + col * factor, y + row * factor, factor, factor);
            }
        }
    }

    /// <summary>EAN-13 si el código lo es; si no, CODE128. Con los números debajo.</summary>
    private static void DrawBarcode(Graphics g, string code, Font font, int width, int y, int barHeight)
    {
        BitMatrix matrix;
        try
        {
            matrix = Pos.Core.Printing.Ean13.IsValid(code)
                ? new EAN13Writer().encode(code, BarcodeFormat.EAN_13, 0, 1)
                : new Code128Writer().encode(code, BarcodeFormat.CODE_128, 0, 1);
        }
        catch (ArgumentException)
        {
            DrawText(g, code, font, ReceiptAlign.Center, width, y);
            return;
        }
        var module = Math.Max(1, Math.Min(4, width * 9 / 10 / matrix.Width));
        var x0 = (width - matrix.Width * module) / 2;
        for (var x = 0; x < matrix.Width; x++)
        {
            if (matrix[x, 0])
                g.FillRectangle(Brushes.Black, x0 + x * module, y, module, barHeight);
        }
        DrawText(g, code, font, ReceiptAlign.Center, width, y + barHeight + 2);
    }

    private static Size LogoSize(string path, int width, double scale)
    {
        try
        {
            using var image = Image.FromFile(path);
            var maxHeight = 240 * scale;
            var factor = Math.Min(1.0, Math.Min(width / (double)image.Width, maxHeight / image.Height));
            return new Size((int)(image.Width * factor), (int)(image.Height * factor));
        }
        catch (Exception e) when (e is IOException or OutOfMemoryException or ArgumentException)
        {
            return Size.Empty; // logo dañado: sale sin logo, como en la térmica
        }
    }

    private static void DrawLogo(Graphics g, string path, int width, int y, double scale)
    {
        var size = LogoSize(path, width, scale);
        if (size.IsEmpty)
            return;
        using var image = Image.FromFile(path);
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.DrawImage(image, (width - size.Width) / 2, y, size.Width, size.Height);
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
    }
}
