using Pos.Core.Domain;
using Pos.Core.Invoicing;
using Pos.Core.Localization;
using Pos.Core.Pricing;
using Pos.Localization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Pos.Modules.Invoicing;

/// <summary>
/// Factura en PDF (A4) para enviarla al cliente (FAC-06) o a la gestoría.
/// Incluye los datos que exige el reglamento de facturación para una factura completa:
/// número y serie, fecha, emisor y cliente con NIF y domicilio, precio unitario sin IVA,
/// base, tipo y cuota por cada tipo de IVA y total.
/// </summary>
public sealed class InvoicePdf(ILocalizer localizer, RegionFormatter formatter)
{
    static InvoicePdf()
    {
        // Licencia Community: gratuita para empresas con ingresos anuales inferiores a 1 M$.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public byte[] Render(InvoiceDocument doc) => Build(doc).GeneratePdf();

    public void Save(InvoiceDocument doc, string path) => Build(doc).GeneratePdf(path);

    private Document Build(InvoiceDocument doc)
    {
        var L = localizer;
        string Money(decimal amount) => formatter.FormatMoney(amount);
        string Amount(decimal amount) => formatter.FormatAmount(amount);

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(18, Unit.Millimetre);
            // Segoe UI para alfabetos latinos y Microsoft YaHei para el chino (fuentes de Windows 11).
            page.DefaultTextStyle(t => t.FontFamily("Segoe UI", "Microsoft YaHei").FontSize(10));

            page.Header().Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    col.Item().Text(doc.IssuerName).FontSize(16).Bold();
                    col.Item().Text($"{L["NifLabel"]} {doc.IssuerNif}");
                    col.Item().Text(doc.IssuerAddress);
                });
                row.RelativeItem().AlignRight().Column(col =>
                {
                    col.Item().AlignRight().Text(L[doc.Type == InvoiceType.Complete ? "InvoiceComplete" : "InvoiceSimplified"])
                        .FontSize(16).Bold();
                    col.Item().AlignRight().Text($"{L["InvoiceNumber"]} {doc.Code}");
                    col.Item().AlignRight().Text($"{L["InvoiceDate"]} {formatter.FormatDateTime(doc.IssuedAtUtc.ToLocalTime())}");
                });
            });

            page.Content().PaddingVertical(16).Column(col =>
            {
                if (doc.CustomerName is not null)
                {
                    col.Item().Background(Colors.Grey.Lighten4).Padding(10).Column(c =>
                    {
                        c.Item().Text(L["InvoiceCustomer"]).Bold();
                        c.Item().Text(doc.CustomerName);
                        c.Item().Text($"{L["NifLabel"]} {doc.CustomerNif}");
                        c.Item().Text(doc.CustomerAddress ?? "");
                    });
                }

                if (doc.ReplacesCode is not null)
                    col.Item().PaddingTop(8).Text(string.Format(L["InvoiceReplaces"], doc.ReplacesCode)).Italic();

                col.Item().PaddingTop(12).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(5);
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        static IContainer Head(IContainer c) => c.BorderBottom(1).PaddingVertical(4);
                        h.Cell().Element(Head).Text(L["InvoiceDescription"]).Bold();
                        h.Cell().Element(Head).AlignRight().Text(L["InvoiceQuantity"]).Bold();
                        h.Cell().Element(Head).AlignRight().Text(L["InvoiceUnitPriceNoVat"]).Bold();
                        h.Cell().Element(Head).AlignRight().Text(L["InvoiceVatRate"]).Bold();
                        h.Cell().Element(Head).AlignRight().Text(L["InvoiceLineTotal"]).Bold();
                    });
                    foreach (var line in doc.Lines)
                    {
                        static IContainer Cell(IContainer c) => c.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3);
                        table.Cell().Element(Cell).Text(line.Description);
                        table.Cell().Element(Cell).AlignRight().Text(line.Quantity.ToString(L.Culture));
                        table.Cell().Element(Cell).AlignRight().Text(Amount(NetUnitPrice(line)));
                        table.Cell().Element(Cell).AlignRight().Text($"{line.VatRate:0.##} %");
                        table.Cell().Element(Cell).AlignRight().Text(Amount(line.Total));
                    }
                });

                col.Item().PaddingTop(16).AlignRight().Width(300).Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn();
                        c.RelativeColumn();
                        c.RelativeColumn();
                        c.RelativeColumn();
                    });
                    table.Header(h =>
                    {
                        static IContainer Head(IContainer c) => c.BorderBottom(1).PaddingVertical(4);
                        h.Cell().Element(Head).Text(L["InvoiceVatRate"]).Bold();
                        h.Cell().Element(Head).AlignRight().Text(L["InvoiceBase"]).Bold();
                        h.Cell().Element(Head).AlignRight().Text(L["InvoiceVatAmount"]).Bold();
                        h.Cell().Element(Head).AlignRight().Text(L["Total"]).Bold();
                    });
                    foreach (var vat in doc.VatLines)
                    {
                        table.Cell().PaddingVertical(2).Text($"{vat.Rate:0.##} %");
                        table.Cell().PaddingVertical(2).AlignRight().Text(Amount(vat.Base));
                        table.Cell().PaddingVertical(2).AlignRight().Text(Amount(vat.VatAmount));
                        table.Cell().PaddingVertical(2).AlignRight().Text(Amount(vat.Total));
                    }
                });

                col.Item().PaddingTop(12).AlignRight().Text($"{L["Total"]}  {Money(doc.Total)}").FontSize(16).Bold();
            });

            page.Footer().AlignCenter().Text(t =>
            {
                t.CurrentPageNumber();
                t.Span(" / ");
                t.TotalPages();
            });
        }));
    }

    /// <summary>Precio unitario sin IVA, que exige la factura completa.</summary>
    private static decimal NetUnitPrice(InvoiceDocumentLine line) =>
        Math.Round(line.UnitPrice / (1 + line.VatRate / 100m), 2, MidpointRounding.AwayFromZero);
}
