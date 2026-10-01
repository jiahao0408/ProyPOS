using Pos.Core.Localization;
using Pos.Localization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Pos.Modules.DataTransfer;

/// <summary>
/// FAC-05: libro de facturas emitidas de un periodo en PDF, para la gestoría: cada factura con su base,
/// cuota y total, y al final los totales por tipo de IVA. En el idioma de la interfaz.
/// </summary>
public sealed class InvoiceBookPdf(ILocalizer localizer, RegionFormatter formatter, ExportService exports)
{
    static InvoiceBookPdf()
    {
        // Licencia Community: gratuita para empresas con ingresos anuales inferiores a 1 M$.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public int Save(string path, DateOnly from, DateOnly to)
    {
        var book = exports.LoadInvoiceBook(from, to);
        Build(book).GeneratePdf(path);
        return book.Rows.Count;
    }

    public byte[] Render(InvoiceBook book) => Build(book).GeneratePdf();

    private Document Build(InvoiceBook book)
    {
        var L = localizer;
        string Amount(decimal a) => formatter.FormatAmount(a);
        string Day(DateOnly d) => formatter.FormatDate(d.ToDateTime(TimeOnly.MinValue));

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(14, Unit.Millimetre);
            page.DefaultTextStyle(t => t.FontFamily("Segoe UI", "Microsoft YaHei").FontSize(9));

            page.Header().Column(col =>
            {
                col.Item().Text(L["InvoiceBookTitle"]).FontSize(16).Bold();
                col.Item().Text($"{book.IssuerName}   {L["NifLabel"]} {book.IssuerNif}");
                col.Item().Text(string.Format(L["PeriodFromTo"], Day(book.From), Day(book.To)));
            });

            page.Content().PaddingVertical(10).Column(col =>
            {
                col.Item().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);   // fecha
                        c.RelativeColumn(2.2f); // número
                        c.RelativeColumn(1.6f); // tipo
                        c.RelativeColumn(3);   // cliente
                        c.RelativeColumn(1.2f); // tipo de IVA
                        c.RelativeColumn(1.5f); // base
                        c.RelativeColumn(1.5f); // cuota
                        c.RelativeColumn(1.6f); // total
                    });
                    table.Header(h =>
                    {
                        static IContainer Head(IContainer c) => c.BorderBottom(1).PaddingVertical(3);
                        h.Cell().Element(Head).Text(L["ColDate"]).Bold();
                        h.Cell().Element(Head).Text(L["ColInvoice"]).Bold();
                        h.Cell().Element(Head).Text(L["ColKind"]).Bold();
                        h.Cell().Element(Head).Text(L["InvoiceCustomer"]).Bold();
                        h.Cell().Element(Head).AlignRight().Text(L["InvoiceVatRate"]).Bold();
                        h.Cell().Element(Head).AlignRight().Text(L["InvoiceBase"]).Bold();
                        h.Cell().Element(Head).AlignRight().Text(L["InvoiceVatAmount"]).Bold();
                        h.Cell().Element(Head).AlignRight().Text(L["ColTotal"]).Bold();
                    });

                    static IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2);
                    foreach (var row in book.Rows)
                    {
                        // Una fila por tipo de IVA de la factura; el total, en la primera.
                        var rates = row.ByRate.Keys.OrderByDescending(r => r).ToList();
                        if (rates.Count == 0)
                            rates.Add(0m);
                        for (var i = 0; i < rates.Count; i++)
                        {
                            var (b, v) = row.ByRate.GetValueOrDefault(rates[i]);
                            var first = i == 0;
                            var replaced = !row.CountsInTotals;
                            table.Cell().Element(Cell).Text(first ? formatter.FormatDateTime(row.IssuedAtLocal) : "");
                            table.Cell().Element(Cell).Text(first ? row.Code : "");
                            table.Cell().Element(Cell).Text(first ? TypeText(row) : "");
                            table.Cell().Element(Cell).Text(first ? Customer(row) : "");
                            table.Cell().Element(Cell).AlignRight().Text($"{rates[i]:0.##} %");
                            // Un ticket sustituido sale tachado: no suma en los totales.
                            table.Cell().Element(Cell).AlignRight().Text(t => Strike(t.Span(Amount(b)), replaced));
                            table.Cell().Element(Cell).AlignRight().Text(t => Strike(t.Span(Amount(v)), replaced));
                            table.Cell().Element(Cell).AlignRight().Text(t => Strike(t.Span(first ? Amount(row.Total) : ""), replaced));
                        }
                    }
                });

                // Totales por tipo de IVA
                col.Item().PaddingTop(14).AlignRight().Width(320).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(1.4f);
                        c.RelativeColumn(1.4f);
                    });
                    static IContainer Head(IContainer c) => c.BorderBottom(1).PaddingVertical(3);
                    table.Cell().Element(Head).Text(L["InvoiceVatRate"]).Bold();
                    table.Cell().Element(Head).AlignRight().Text(L["InvoiceBase"]).Bold();
                    table.Cell().Element(Head).AlignRight().Text(L["InvoiceVatAmount"]).Bold();
                    table.Cell().Element(Head).AlignRight().Text(L["ColTotal"]).Bold();
                    foreach (var rate in book.Rates)
                    {
                        var (b, v) = book.TotalFor(rate);
                        table.Cell().PaddingVertical(2).Text($"{rate:0.##} %");
                        table.Cell().PaddingVertical(2).AlignRight().Text(Amount(b));
                        table.Cell().PaddingVertical(2).AlignRight().Text(Amount(v));
                        table.Cell().PaddingVertical(2).AlignRight().Text(Amount(b + v));
                    }
                    table.Cell().BorderTop(1).PaddingVertical(3).Text(L["TotalRow"]).Bold();
                    table.Cell().BorderTop(1).PaddingVertical(3).AlignRight().Text(Amount(book.Rates.Sum(r => book.TotalFor(r).Base))).Bold();
                    table.Cell().BorderTop(1).PaddingVertical(3).AlignRight().Text(Amount(book.Rates.Sum(r => book.TotalFor(r).Vat))).Bold();
                    table.Cell().BorderTop(1).PaddingVertical(3).AlignRight().Text(formatter.FormatMoney(book.Total)).Bold();
                });

                col.Item().PaddingTop(6).AlignRight().Text(string.Format(L["InvoiceBookCount"], book.Rows.Count));
                if (book.Rows.Any(r => !r.CountsInTotals))
                    col.Item().PaddingTop(4).Text(L["InvoiceBookReplacedNote"]).Italic().FontSize(8);
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        }));
    }

    private string TypeText(InvoiceBookRow row)
    {
        var text = exports.TypeName(row.Type);
        if (row.RectifiesCode is { } rectified)
            text += $" ({rectified})";
        if (row.ReplacedBy is { } replacedBy)
            text += " " + string.Format(localizer["InvoiceReplacedBy"], replacedBy);
        return text;
    }

    private static void Strike(TextSpanDescriptor span, bool strike)
    {
        if (strike)
            span.Strikethrough().FontColor(Colors.Grey.Medium);
    }

    private static string Customer(InvoiceBookRow row) =>
        row.CustomerName is null ? "" : $"{row.CustomerName} ({row.CustomerNif})";
}
