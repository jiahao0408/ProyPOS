using System.Globalization;
using System.Text;
using ClosedXML.Excel;

namespace Pos.Modules.DataTransfer;

/// <summary>Lee y escribe tablas en CSV o Excel (.xlsx). La primera fila son los encabezados.</summary>
public static class TabularFile
{
    static TabularFile() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static bool IsExcel(string path) => Path.GetExtension(path).Equals(".xlsx", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string[]> Read(string path) => IsExcel(path) ? ReadExcel(path) : ReadCsv(path);

    public static void Write(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows) =>
        Write(path, headers, rows, CultureInfo.InvariantCulture);

    /// <summary>
    /// Escribe valores con tipo: en Excel los importes (decimal) y las cantidades (int) son números y las
    /// fechas son fechas, para poder sumarlos; en CSV se escriben con el formato de <paramref name="culture"/>
    /// (en español, "12,50"), que es lo que espera Excel al abrirlo con doble clic.
    /// </summary>
    public static void Write(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows, CultureInfo culture)
    {
        if (IsExcel(path))
            WriteExcel(path, headers, rows);
        else
            WriteCsv(path, headers, rows.Select(r => (IReadOnlyList<string>)r.Select(v => CsvText(v, culture)).ToList()));
    }

    private static string CsvText(object? value, CultureInfo culture) => value switch
    {
        null => "",
        decimal d => d.ToString("0.00", culture),
        DateTime t => t.ToString(t.TimeOfDay == TimeSpan.Zero ? "d" : "g", culture),
        IFormattable f => f.ToString(null, culture),
        _ => value.ToString() ?? "",
    };

    private static IReadOnlyList<string[]> ReadExcel(string path)
    {
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheets.First();
        var range = sheet.RangeUsed();
        if (range is null)
            return [];

        var columns = range.ColumnCount();
        return range.Rows()
            .Select(row => Enumerable.Range(1, columns).Select(c => CellText(row.Cell(c))).ToArray())
            .Where(cells => cells.Any(c => c.Length > 0))
            .ToList();
    }

    /// <summary>Los números de Excel se leen con punto decimal, sin depender del idioma de Windows.</summary>
    private static string CellText(IXLCell cell) =>
        cell.DataType == XLDataType.Number
            ? cell.GetDouble().ToString(CultureInfo.InvariantCulture)
            : cell.GetFormattedString().Trim();

    private static void WriteExcel(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<object?>> rows)
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Datos");
        for (var c = 0; c < headers.Count; c++)
            sheet.Cell(1, c + 1).Value = headers[c];
        sheet.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var row in rows)
        {
            for (var c = 0; c < row.Count; c++)
                SetCell(sheet.Cell(r, c + 1), row[c]);
            r++;
        }
        sheet.Columns().AdjustToContents();
        workbook.SaveAs(path);
    }

    private static void SetCell(IXLCell cell, object? value)
    {
        switch (value)
        {
            case null:
                break;
            case decimal d:
                cell.Value = d;
                cell.Style.NumberFormat.Format = "#,##0.00";
                break;
            case int i:
                cell.Value = i;
                break;
            case DateTime t:
                cell.Value = t;
                cell.Style.DateFormat.Format = t.TimeOfDay == TimeSpan.Zero ? "dd/mm/yyyy" : "dd/mm/yyyy hh:mm";
                break;
            default:
                cell.Value = value.ToString();
                break;
        }
    }

    /// <summary>
    /// CSV con ";" o "," (se detecta en la primera línea) y comillas. Excel en español guarda
    /// los CSV en Windows-1252, no en UTF-8: si el fichero no es UTF-8 válido, se lee así.
    /// </summary>
    private static IReadOnlyList<string[]> ReadCsv(string path)
    {
        var bytes = File.ReadAllBytes(path);
        string text;
        try
        {
            text = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            text = Encoding.GetEncoding(1252).GetString(bytes);
        }
        text = text.TrimStart('﻿');

        var firstLine = text.Split('\n', 2)[0];
        var delimiter = firstLine.Count(c => c == ';') >= firstLine.Count(c => c == ',') ? ';' : ',';
        return ParseCsv(text, delimiter).Where(r => r.Any(c => c.Length > 0)).ToList();
    }

    public static IEnumerable<string[]> ParseCsv(string text, char delimiter)
    {
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                    inQuotes = false;
                else
                    field.Append(c);
            }
            else if (c == '"')
                inQuotes = true;
            else if (c == delimiter)
            {
                row.Add(field.ToString().Trim());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString().Trim());
                field.Clear();
                yield return row.ToArray();
                row.Clear();
            }
            else if (c != '\r')
                field.Append(c);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString().Trim());
            yield return row.ToArray();
        }
    }

    /// <summary>CSV en UTF-8 con BOM y ";" para que Excel en español lo abra bien con doble clic.</summary>
    private static void WriteCsv(string path, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows) =>
        File.WriteAllText(path, ToCsv(headers, rows), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    /// <summary>Texto CSV con ";" y comillas donde hace falta.</summary>
    public static string ToCsv(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        static string Quote(string s) => s.IndexOfAny([';', '"', '\n', '\r']) >= 0 ? $"\"{s.Replace("\"", "\"\"")}\"" : s;

        var sb = new StringBuilder();
        sb.AppendLine(string.Join(';', headers.Select(Quote)));
        foreach (var row in rows)
            sb.AppendLine(string.Join(';', row.Select(Quote)));
        return sb.ToString();
    }
}
