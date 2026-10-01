using Pos.Core.Localization;
using Pos.Core.Printing;
using Pos.Data;
using Pos.Localization;
using static Pos.Modules.Printing.ReceiptText2;

namespace Pos.Modules.Printing;

/// <summary>Cierre Z (CAJ-02) y etiquetas de precio (BAZ-01).</summary>
public sealed class ReportBuilder(PrintLocalization print)
{
    public IReadOnlyList<ReceiptElement> BuildZReport(ZReportDocument z, BusinessProfile business, PrinterProfile printer)
    {
        var (L, formatter) = print.For();
        var width = printer.CharsPerLine;
        string Amount(decimal a) => formatter.FormatAmount(a);
        string Row(string label, decimal value) => Columns(label, Amount(value), width);

        var r = new List<ReceiptElement>
        {
            new ReceiptText(business.Name, ReceiptAlign.Center, Bold: true),
            new ReceiptText($"{L["NifLabel"]} {business.Nif}", ReceiptAlign.Center),
            new ReceiptSeparator(),
            new ReceiptText(string.Format(L["ZReportTitle"], z.ZNumber), ReceiptAlign.Center, Bold: true, Large: true),
            new ReceiptText(Columns(L["ZOpened"], formatter.FormatDateTime(z.OpenedAtUtc.ToLocalTime()), width)),
            new ReceiptText(Columns(L["ZClosed"], formatter.FormatDateTime(z.ClosedAtUtc.ToLocalTime()), width)),
            new ReceiptText(Columns(L["Cashier"], z.ClosedBy, width)),
            new ReceiptSeparator(),
            new ReceiptText(Columns(L["ZSalesCount"], z.SalesCount.ToString(L.Culture), width)),
            new ReceiptText(Row(L["ZSalesTotal"], z.SalesTotal), Bold: true),
            new ReceiptText(Row(L["PayCash"], z.CashTotal)),
            new ReceiptText(Row(L["PayCard"], z.CardTotal)),
            new ReceiptSeparator(),
            new ReceiptText(Columns(L["InvoiceVatRate"], $"{L["InvoiceBase"]} / {L["InvoiceVatAmount"]}", width), Bold: true),
        };
        foreach (var vat in z.VatLines)
            r.Add(new ReceiptText(Columns($"{vat.Rate:0.##}%", $"{Amount(vat.Base)} / {Amount(vat.VatAmount)}", width)));

        r.Add(new ReceiptSeparator());
        r.Add(new ReceiptText(Row(L["OpeningFloat"], z.OpeningFloat)));
        r.Add(new ReceiptText(Row(L["ZCashSales"], z.CashTotal)));
        r.Add(new ReceiptText(Row(L["ZExpectedCash"], z.ExpectedCash), Bold: true));
        r.Add(new ReceiptText(Row(L["ZCountedCash"], z.CountedCash), Bold: true));
        r.Add(new ReceiptText(Row(L["ZDifference"], z.Difference), Bold: true, Large: z.Difference != 0));
        r.Add(new ReceiptCut());
        return r;
    }

    /// <summary>Una etiqueta por copia: nombre, precio grande y código de barras, separadas por un corte.</summary>
    public IReadOnlyList<ReceiptElement> BuildLabels(IEnumerable<LabelItem> items, PrinterProfile printer)
    {
        var (_, formatter) = print.For();
        var width = printer.CharsPerLine;
        var r = new List<ReceiptElement>();
        foreach (var item in items)
        {
            for (var i = 0; i < item.Copies; i++)
            {
                foreach (var line in Wrap(item.Name, width).Take(2))
                    r.Add(new ReceiptText(line, ReceiptAlign.Center, Bold: true));
                r.Add(new ReceiptText(formatter.FormatMoney(item.Price), ReceiptAlign.Center, Bold: true, Large: true));
                r.Add(new ReceiptBarcode(item.Barcode));
                r.Add(new ReceiptCut());
            }
        }
        return r;
    }
}
