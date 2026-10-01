using Pos.Core.Domain;
using Pos.Core.Invoicing;
using Pos.Core.Localization;
using Pos.Data;
using Pos.Localization;
using static Pos.Modules.Printing.ReceiptText2;

namespace Pos.Modules.Printing;

/// <summary>
/// Maqueta el ticket de una factura (IMP-01/02/03, FAC-01/02/06).
/// Los datos fiscales salen de la factura (tal como se emitió); el logo, el teléfono
/// y el mensaje del pie, de los ajustes actuales del negocio.
/// </summary>
public sealed class ReceiptBuilder(ILocalizer localizer, RegionFormatter formatter)
{
    public IReadOnlyList<ReceiptElement> Build(InvoiceDocument doc, BusinessProfile business, PrinterProfile printer, bool copy)
    {
        var L = localizer;
        var width = printer.CharsPerLine;
        string Amount(decimal a) => formatter.FormatAmount(a);
        var r = new List<ReceiptElement>();

        // --- Cabecera (IMP-03) ---
        if (business.LogoPath is { } logo && File.Exists(logo))
            r.Add(new ReceiptLogo(logo));
        r.Add(new ReceiptText(doc.IssuerName, ReceiptAlign.Center, Bold: true, Large: doc.IssuerName.Length <= width / 2));
        r.Add(new ReceiptText($"{L["NifLabel"]} {doc.IssuerNif}", ReceiptAlign.Center));
        foreach (var line in Wrap(doc.IssuerAddress, width))
            r.Add(new ReceiptText(line, ReceiptAlign.Center));
        if (business.Phone.Length > 0)
            r.Add(new ReceiptText($"{L["PhoneLabel"]} {business.Phone}", ReceiptAlign.Center));
        r.Add(new ReceiptSeparator());

        // --- Datos de la factura (FAC-01) ---
        if (copy)
            r.Add(new ReceiptText(L["CopyMark"], ReceiptAlign.Center, Bold: true, Large: true)); // IMP-02
        r.Add(new ReceiptText(L[TitleKey(doc.Type)], ReceiptAlign.Center, Bold: true));
        r.Add(new ReceiptText(Columns(L["InvoiceNumber"], doc.Code, width)));
        r.Add(new ReceiptText(Columns(L["InvoiceDate"], formatter.FormatDateTime(doc.IssuedAtUtc.ToLocalTime()), width)));
        if (doc.CashierName.Length > 0)
            r.Add(new ReceiptText(Columns(L["Cashier"], doc.CashierName, width)));

        // --- Cliente (FAC-02, FAC-06) ---
        if (doc.CustomerName is not null)
        {
            r.Add(new ReceiptSeparator());
            r.Add(new ReceiptText(L["InvoiceCustomer"], Bold: true));
            foreach (var line in Wrap(doc.CustomerName, width))
                r.Add(new ReceiptText(line));
            r.Add(new ReceiptText($"{L["NifLabel"]} {doc.CustomerNif}"));
            foreach (var line in Wrap(doc.CustomerAddress ?? "", width))
                r.Add(new ReceiptText(line));
        }
        if (doc.ReplacesCode is not null)
            foreach (var line in Wrap(string.Format(L["InvoiceReplaces"], doc.ReplacesCode), width))
                r.Add(new ReceiptText(line));
        if (doc.RectifiesCode is not null)
        {
            // FAC-03: referencia a la factura original y motivo de la devolución.
            foreach (var line in Wrap(string.Format(L["InvoiceRectifies"], doc.RectifiesCode), width))
                r.Add(new ReceiptText(line, Bold: true));
            if (doc.ReturnReason is { Length: > 0 } reason)
                foreach (var line in Wrap($"{L["ReturnReason"]}: {reason}", width))
                    r.Add(new ReceiptText(line));
        }
        r.Add(new ReceiptSeparator());

        // --- Líneas ---
        var showRates = doc.VatLines.Count > 1; // con varios tipos, cada línea indica el suyo
        foreach (var line in doc.Lines)
        {
            var qty = line.Quantity == 1 ? "" : $"{line.Quantity} x ";
            var rate = showRates ? $" ({line.VatRate:0.##}%)" : "";
            r.Add(new ReceiptText(Columns(qty + line.Description, Amount(line.Total), width)));
            // Precio por unidad: el de tarifa (el descuento va en su propia línea); en una devolución, lo que se devuelve de verdad.
            var unit = line.Quantity < 0 ? line.EffectiveUnitPrice : line.UnitPrice;
            if (line.Quantity != 1 || rate.Length > 0)
                r.Add(new ReceiptText($"   {Amount(unit)}{rate}"));
            if (line.Discount > 0)
                r.Add(new ReceiptText($"   {L["DiscountShort"]} -{Amount(line.Discount)}"));
            if (doc.Type == InvoiceType.Complete)
                r.Add(new ReceiptText($"   {L["InvoiceUnitPriceNoVat"]}: {Amount(NetUnitPrice(line))}"));
        }
        r.Add(new ReceiptSeparator());

        // --- Total y desglose de IVA (FAC-01: base, IVA y total) ---
        r.Add(new ReceiptText(Columns(L["Total"].ToUpperInvariant(), formatter.FormatMoney(doc.Total), width / 2), Bold: true, Large: true));
        r.Add(new ReceiptText(L["VatIncluded"], ReceiptAlign.Right));
        r.Add(new ReceiptBlankLine());
        r.Add(new ReceiptText(VatRow(L["InvoiceVatRate"], L["InvoiceBase"], L["InvoiceVatAmount"], L["Total"], width), Bold: true));
        foreach (var vat in doc.VatLines)
            r.Add(new ReceiptText(VatRow($"{vat.Rate:0.##}%", Amount(vat.Base), Amount(vat.VatAmount), Amount(vat.Total), width)));
        r.Add(new ReceiptSeparator());

        // --- Pagos ---
        foreach (var payment in doc.Payments)
            r.Add(new ReceiptText(Columns(L[PaymentKey(payment.Method)], Amount(payment.Amount), width)));
        if (doc.CashTendered > 0)
        {
            r.Add(new ReceiptText(Columns(L["CashTendered"], Amount(doc.CashTendered), width)));
            r.Add(new ReceiptText(Columns(L["Change"], Amount(doc.Change), width)));
        }

        // --- Pie (IMP-03) y QR para volver a encontrar el ticket (FAC-06) ---
        if (business.FooterMessage.Length > 0)
        {
            r.Add(new ReceiptBlankLine());
            foreach (var line in business.FooterMessage.Split('\n'))
                foreach (var wrapped in Wrap(line.Trim(), width))
                    r.Add(new ReceiptText(wrapped, ReceiptAlign.Center));
        }
        // FAC-04: QR de cotejo de la AEAT (si no hay, el número de factura para encontrarla luego, FAC-06).
        r.Add(new ReceiptBlankLine());
        r.Add(new ReceiptText(L["QrHint"], ReceiptAlign.Center));
        r.Add(new ReceiptQr(doc.QrUrl ?? doc.Code));
        if (doc.VerifactuMode)
            r.Add(new ReceiptText(Pos.Core.Verifactu.VerifactuQr.VerifactuLegend, ReceiptAlign.Center, Bold: true));
        r.Add(new ReceiptCut());
        return r;
    }

    /// <summary>
    /// BAZ-07: ticket regalo, sin precios. Lleva el número de la factura (también en el QR) para poder
    /// buscar la venta y hacer un cambio. No lleva el QR de la AEAT porque incluye el importe.
    /// </summary>
    public IReadOnlyList<ReceiptElement> BuildGift(InvoiceDocument doc, BusinessProfile business, PrinterProfile printer)
    {
        var L = localizer;
        var width = printer.CharsPerLine;
        var r = new List<ReceiptElement>();
        if (business.LogoPath is { } logo && File.Exists(logo))
            r.Add(new ReceiptLogo(logo));
        r.Add(new ReceiptText(doc.IssuerName, ReceiptAlign.Center, Bold: true, Large: doc.IssuerName.Length <= width / 2));
        foreach (var line in Wrap(doc.IssuerAddress, width))
            r.Add(new ReceiptText(line, ReceiptAlign.Center));
        r.Add(new ReceiptSeparator());
        r.Add(new ReceiptText(L["GiftTicket"], ReceiptAlign.Center, Bold: true, Large: true));
        r.Add(new ReceiptText(Columns(L["InvoiceNumber"], doc.Code, width)));
        r.Add(new ReceiptText(Columns(L["InvoiceDate"], formatter.FormatDate(doc.IssuedAtUtc.ToLocalTime()), width)));
        r.Add(new ReceiptSeparator());
        foreach (var line in doc.Lines)
            foreach (var wrapped in Wrap($"{line.Quantity} x {line.Description}", width))
                r.Add(new ReceiptText(wrapped));
        r.Add(new ReceiptSeparator());
        foreach (var line in Wrap(L["GiftTicketHint"], width))
            r.Add(new ReceiptText(line, ReceiptAlign.Center));
        if (business.FooterMessage.Length > 0)
            foreach (var line in business.FooterMessage.Split('\n'))
                foreach (var wrapped in Wrap(line.Trim(), width))
                    r.Add(new ReceiptText(wrapped, ReceiptAlign.Center));
        r.Add(new ReceiptBlankLine());
        r.Add(new ReceiptQr(doc.Code));
        r.Add(new ReceiptCut());
        return r;
    }

    public static string TitleKey(InvoiceType type) => type switch
    {
        InvoiceType.Complete => "InvoiceComplete",
        InvoiceType.Rectificative => "InvoiceRectificative",
        _ => "InvoiceSimplified",
    };

    public static string PaymentKey(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "PayCash",
        PaymentMethod.Card => "PayCard",
        _ => "PayStoreCredit",
    };

    /// <summary>Ticket de prueba para el botón "Probar impresora" (HW-01).</summary>
    public IReadOnlyList<ReceiptElement> BuildTest(BusinessProfile business, PrinterProfile printer)
    {
        var width = printer.CharsPerLine;
        var r = new List<ReceiptElement>();
        if (business.LogoPath is { } logo && File.Exists(logo))
            r.Add(new ReceiptLogo(logo));
        r.Add(new ReceiptText(localizer["PrinterTestTitle"], ReceiptAlign.Center, Bold: true, Large: true));
        r.Add(new ReceiptText(business.Name, ReceiptAlign.Center));
        r.Add(new ReceiptSeparator());
        r.Add(new ReceiptText(Columns($"{printer.PaperWidthMm} mm", $"{width} col.", width)));
        r.Add(new ReceiptText("áéíóú ñÑ çÇ àè €"));
        r.Add(new ReceiptText(new string('0', width).Select((_, i) => (char)('0' + i % 10)).Aggregate("", (s, c) => s + c)));
        r.Add(new ReceiptText(Columns(localizer["Total"], formatter.FormatMoney(1234.5m), width)));
        r.Add(new ReceiptQr("StarSeaPOS"));
        r.Add(new ReceiptCut());
        return r;
    }

    private static string VatRow(string rate, string @base, string vat, string total, int width)
    {
        var col = width / 4;
        return rate.PadRight(width - 3 * col) + @base.PadLeft(col) + vat.PadLeft(col) + total.PadLeft(col);
    }

    private static decimal NetUnitPrice(InvoiceDocumentLine line) =>
        Math.Round(line.UnitPrice / (1 + line.VatRate / 100m), 2, MidpointRounding.AwayFromZero);
}
