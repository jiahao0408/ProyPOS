namespace Pos.Modules.Printing;

public enum ReceiptAlign
{
    Left,
    Center,
    Right,
}

/// <summary>Elemento de un ticket, independiente de la impresora (se convierte a ESC/POS o a texto).</summary>
public abstract record ReceiptElement;

/// <summary>Una línea de texto. Grande = doble de ancho y alto (cabe la mitad de caracteres).</summary>
public sealed record ReceiptText(string Text, ReceiptAlign Align = ReceiptAlign.Left, bool Bold = false, bool Large = false) : ReceiptElement;

public sealed record ReceiptSeparator : ReceiptElement;

public sealed record ReceiptBlankLine : ReceiptElement;

public sealed record ReceiptQr(string Data) : ReceiptElement;

public sealed record ReceiptLogo(string Path) : ReceiptElement;

/// <summary>Código de barras con los números debajo: EAN-13 si es válido, si no CODE128 (BAZ-01).</summary>
public sealed record ReceiptBarcode(string Code) : ReceiptElement;

/// <summary>Avanza el papel y lo corta.</summary>
public sealed record ReceiptCut : ReceiptElement;

/// <summary>Ayudas para maquetar líneas de ancho fijo.</summary>
public static class ReceiptText2
{
    /// <summary>Texto a la izquierda y a la derecha en la misma línea; si no caben, el izquierdo se corta.</summary>
    public static string Columns(string left, string right, int width)
    {
        var space = width - right.Length - 1;
        if (space < 1)
            return right.Length > width ? right[..width] : right.PadLeft(width);
        if (left.Length > space)
            left = left[..space];
        return left.PadRight(width - right.Length) + right;
    }

    /// <summary>Parte un texto largo en líneas del ancho dado, por palabras.</summary>
    public static IEnumerable<string> Wrap(string text, int width)
    {
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var w = word;
            while (w.Length > width)
            {
                if (line.Length > 0)
                {
                    yield return line;
                    line = "";
                }
                yield return w[..width];
                w = w[width..];
            }
            if (line.Length == 0)
                line = w;
            else if (line.Length + 1 + w.Length <= width)
                line += " " + w;
            else
            {
                yield return line;
                line = w;
            }
        }
        if (line.Length > 0)
            yield return line;
    }
}
