using System.Text;
using Pos.Data;

namespace Pos.Modules.Printing;

/// <summary>
/// Convierte un ticket a comandos ESC/POS (compatibles con Epson y la mayoría de impresoras
/// térmicas de 58 y 80 mm). HW-01. v1.1: tabla de caracteres, corte y QR según el modelo.
/// </summary>
public static class EscPosEncoder
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;
    private const byte Fs = 0x1C;
    private const byte Lf = 0x0A;

    static EscPosEncoder() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Número de la tabla en el comando ESC t y página de códigos de Windows equivalente.</summary>
    public static (byte Table, int WindowsCodePage) CodePage(PrinterCodePage codePage) => codePage switch
    {
        PrinterCodePage.Wpc1252 => (16, 1252),
        PrinterCodePage.Pc850 => (2, 850),
        PrinterCodePage.Pc437 => (0, 437),
        _ => (19, 858),
    };

    public static Encoding TextEncoding(PrinterEncoding encoding, PrinterCodePage codePage = PrinterCodePage.Pc858) =>
        encoding == PrinterEncoding.Chinese
            ? Encoding.GetEncoding("GB18030")
            : Encoding.GetEncoding(CodePage(codePage).WindowsCodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);

    /// <summary>
    /// Texto en bytes de la tabla elegida. Lo que no existe en ella se sustituye en vez de salir como "?":
    /// € → EUR, letras acentuadas → sin acento.
    /// </summary>
    public static byte[] EncodeText(string value, Encoding encoding)
    {
        try
        {
            return encoding.GetBytes(value);
        }
        catch (EncoderFallbackException)
        {
            var sb = new StringBuilder(value.Length + 8);
            foreach (var c in value)
                sb.Append(CanEncode(encoding, c) ? c.ToString() : Substitute(c, encoding));
            return encoding.GetBytes(sb.ToString());
        }
    }

    private static bool CanEncode(Encoding encoding, char c)
    {
        try
        {
            encoding.GetBytes(c.ToString());
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static string Substitute(char c, Encoding encoding)
    {
        if (c == '€')
            return "EUR";
        var plain = new string(c.ToString().Normalize(NormalizationForm.FormD)
            .Where(ch => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(ch) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());
        return plain.Length > 0 && plain.All(ch => CanEncode(encoding, ch)) ? plain : "?";
    }

    public static byte[] Encode(IEnumerable<ReceiptElement> receipt, PrinterProfile printer)
    {
        var text = TextEncoding(printer.Encoding, printer.CodePage);
        var o = new List<byte>(4096);
        void AddText(string value) => o.AddRange(EncodeText(value, text));

        o.AddRange([Esc, (byte)'@']); // reiniciar
        if (printer.Encoding == PrinterEncoding.Chinese)
            o.AddRange([Fs, (byte)'&']); // modo de caracteres chinos
        else
            o.AddRange([Esc, (byte)'t', CodePage(printer.CodePage).Table]);

        foreach (var element in receipt)
        {
            switch (element)
            {
                case ReceiptText t:
                    o.AddRange([Esc, (byte)'a', (byte)t.Align]);
                    o.AddRange([Esc, (byte)'E', t.Bold ? (byte)1 : (byte)0]);
                    o.AddRange([Gs, (byte)'!', t.Large ? (byte)0x11 : (byte)0]);
                    AddText(t.Text);
                    o.Add(Lf);
                    o.AddRange([Gs, (byte)'!', 0, Esc, (byte)'E', 0, Esc, (byte)'a', 0]);
                    break;
                case ReceiptSeparator:
                    AddText(new string('-', printer.CharsPerLine));
                    o.Add(Lf);
                    break;
                case ReceiptBlankLine:
                    o.Add(Lf);
                    break;
                case ReceiptQr qr:
                    o.AddRange([Esc, (byte)'a', 1]);
                    if (printer.Qr == PrinterQrMode.Image)
                        AddRaster(o, QrRaster.Render(qr.Data, printer.DotsPerLine)); // v1.1: impresoras sin GS ( k
                    else
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
                case ReceiptDrawerKick kick:
                    // ESC p m t1 t2: pulso en el pin 2 (m = 0) o 5 (m = 1), 50 ms encendido y 500 ms apagado (unidades de 2 ms).
                    o.AddRange([Esc, (byte)'p', kick.Pin == 5 ? (byte)1 : (byte)0, 25, 250]);
                    break;
                case ReceiptCut:
                    switch (printer.Cut)
                    {
                        case PrinterCutMode.Full:
                            o.AddRange([Esc, (byte)'d', 4]);
                            o.AddRange([Gs, (byte)'V', 65, 0]); // corte total
                            break;
                        case PrinterCutMode.None:
                            o.AddRange([Esc, (byte)'d', 6]);    // sin cortador: papel suficiente para arrancarlo
                            break;
                        default:
                            o.AddRange([Esc, (byte)'d', 4]);  // avanzar 4 líneas
                            o.AddRange([Gs, (byte)'V', 66, 0]); // corte parcial
                            break;
                    }
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
