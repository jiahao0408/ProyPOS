using System.Text;
using Pos.Data;

namespace Pos.Modules.Printing;

/// <summary>
/// Convierte un ticket a comandos ESC/POS (compatibles con Epson y la mayoría de impresoras
/// térmicas de 58 y 80 mm). HW-01.
/// </summary>
public static class EscPosEncoder
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;
    private const byte Fs = 0x1C;
    private const byte Lf = 0x0A;

    /// <summary>Número de la tabla PC858 (latín + €) en el comando ESC t de Epson.</summary>
    private const byte Pc858Table = 19;

    static EscPosEncoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static Encoding TextEncoding(PrinterEncoding encoding) =>
        encoding == PrinterEncoding.Chinese
            ? Encoding.GetEncoding("GB18030")
            : Encoding.GetEncoding(858, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);

    public static byte[] Encode(IEnumerable<ReceiptElement> receipt, PrinterProfile printer)
    {
        var text = TextEncoding(printer.Encoding);
        var o = new List<byte>(4096);

        o.AddRange([Esc, (byte)'@']); // reiniciar
        if (printer.Encoding == PrinterEncoding.Chinese)
            o.AddRange([Fs, (byte)'&']); // modo de caracteres chinos
        else
            o.AddRange([Esc, (byte)'t', Pc858Table]);

        foreach (var element in receipt)
        {
            switch (element)
            {
                case ReceiptText t:
                    o.AddRange([Esc, (byte)'a', (byte)t.Align]);
                    o.AddRange([Esc, (byte)'E', t.Bold ? (byte)1 : (byte)0]);
                    o.AddRange([Gs, (byte)'!', t.Large ? (byte)0x11 : (byte)0]);
                    o.AddRange(text.GetBytes(t.Text));
                    o.Add(Lf);
                    o.AddRange([Gs, (byte)'!', 0, Esc, (byte)'E', 0, Esc, (byte)'a', 0]);
                    break;
                case ReceiptSeparator:
                    o.AddRange(text.GetBytes(new string('-', printer.CharsPerLine)));
                    o.Add(Lf);
                    break;
                case ReceiptBlankLine:
                    o.Add(Lf);
                    break;
                case ReceiptQr qr:
                    o.AddRange([Esc, (byte)'a', 1]);
                    AddQr(o, Encoding.UTF8.GetBytes(qr.Data));
                    o.AddRange([Esc, (byte)'a', 0]);
                    break;
                case ReceiptBarcode barcode:
                    o.AddRange([Esc, (byte)'a', 1]);
                    AddBarcode(o, barcode.Code);
                    o.AddRange([Esc, (byte)'a', 0]);
                    break;
                case ReceiptLogo logo:
                    if (LogoRasterizer.TryRasterize(logo.Path, printer.DotsPerLine, out var raster))
                    {
                        o.AddRange([Esc, (byte)'a', 1]);
                        AddRaster(o, raster);
                        o.AddRange([Esc, (byte)'a', 0]);
                    }
                    break;
                case ReceiptDrawerKick:
                    // ESC p m t1 t2: pulso en el pin 2 (m = 0) de 50 ms encendido y 500 ms apagado (unidades de 2 ms).
                    o.AddRange([Esc, (byte)'p', 0, 25, 250]);
                    break;
                case ReceiptCut:
                    o.AddRange([Esc, (byte)'d', 4]);  // avanzar 4 líneas
                    o.AddRange([Gs, (byte)'V', 66, 0]); // corte parcial
                    break;
            }
        }

        if (printer.Encoding == PrinterEncoding.Chinese)
            o.AddRange([Fs, (byte)'.']); // salir del modo chino
        return o.ToArray();
    }

    /// <summary>QR modelo 2, módulo de 6 puntos, corrección M (GS ( k).</summary>
    private static void AddQr(List<byte> o, byte[] data)
    {
        o.AddRange([Gs, (byte)'(', (byte)'k', 4, 0, 49, 65, 50, 0]); // modelo 2
        o.AddRange([Gs, (byte)'(', (byte)'k', 3, 0, 49, 67, 6]);     // tamaño
        o.AddRange([Gs, (byte)'(', (byte)'k', 3, 0, 49, 69, 49]);    // corrección M
        var length = data.Length + 3;
        o.AddRange([Gs, (byte)'(', (byte)'k', (byte)(length % 256), (byte)(length / 256), 49, 80, 48]);
        o.AddRange(data);
        o.AddRange([Gs, (byte)'(', (byte)'k', 3, 0, 49, 81, 48]);    // imprimir
        o.Add(Lf);
    }

    /// <summary>Código de barras (GS k): alto 80 puntos, módulo 2, números debajo.</summary>
    private static void AddBarcode(List<byte> o, string code)
    {
        o.AddRange([Gs, (byte)'h', 80]);
        o.AddRange([Gs, (byte)'w', 2]);
        o.AddRange([Gs, (byte)'H', 2]);
        if (Pos.Core.Printing.Ean13.IsValid(code))
        {
            o.AddRange([Gs, (byte)'k', 67, 12]); // EAN-13: la impresora calcula el dígito de control
            o.AddRange(Encoding.ASCII.GetBytes(code[..12]));
        }
        else
        {
            var data = Encoding.ASCII.GetBytes("{B" + code);
            o.AddRange([Gs, (byte)'k', 73, (byte)data.Length]); // CODE128, juego B
            o.AddRange(data);
        }
        o.Add(Lf);
    }

    /// <summary>Imagen de 1 bit (GS v 0).</summary>
    private static void AddRaster(List<byte> o, MonochromeImage image)
    {
        o.AddRange([Gs, (byte)'v', (byte)'0', 0,
            (byte)(image.BytesPerRow % 256), (byte)(image.BytesPerRow / 256),
            (byte)(image.Height % 256), (byte)(image.Height / 256)]);
        o.AddRange(image.Data);
        o.Add(Lf);
    }
}
