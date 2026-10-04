using System.Text;
using Pos.Core.Domain;
using Pos.Core.Hardware;
using Pos.Core.Invoicing;
using Pos.Core.Printing;
using Pos.Data;

namespace Pos.Modules.Printing;

/// <summary>Resultado de imprimir: null = bien; si no, el motivo técnico para mostrarlo al usuario.</summary>
public sealed record PrintOutcome(string? Error)
{
    public bool Success => Error is null;

    public static PrintOutcome Ok { get; } = new((string?)null);
}

/// <summary>
/// Imprime tickets y facturas (IMP-01, IMP-02) y la prueba de impresora (HW-01).
/// v2: en una impresora normal de Windows (HP, Brother, Canon…) lo mismo se imprime como páginas con su driver;
/// las facturas, en A4 con el diseño del PDF si así se configura.
/// </summary>
public sealed class PrintService(
    IRawPrinter printer,
    IInvoiceDocuments invoices,
    ProfileStore profiles,
    ReceiptBuilder builder,
    ReportBuilder reports,
    IPagePrinter? pagePrinter = null,
    IInvoicePageRenderer? invoicePages = null)
{
    /// <summary>CAJ-02: imprime el cierre Z.</summary>
    public Task<PrintOutcome> PrintZReportAsync(ZReportDocument z, CancellationToken cancellationToken = default)
    {
        var profile = profiles.GetPrinter();
        return SendAsync(reports.BuildZReport(z, profiles.GetBusiness(), profile), profile, cancellationToken);
    }

    public string PreviewZReport(ZReportDocument z) =>
        TextPreview.Render(reports.BuildZReport(z, profiles.GetBusiness(), profiles.GetPrinter()), profiles.GetPrinter());

    /// <summary>BAZ-01: etiquetas de precio en la impresora de etiquetas.</summary>
    public Task<PrintOutcome> PrintLabelsAsync(IReadOnlyList<LabelItem> labels, CancellationToken cancellationToken = default)
    {
        var profile = profiles.GetPrinter(PrinterDestination.Labels);
        return SendAsync(reports.BuildLabels(labels, profile), profile, cancellationToken, PrinterDestination.Labels);
    }

    /// <param name="copy">IMP-02: las reimpresiones llevan la marca "COPIA".</param>
    /// <param name="language">CFG-03: idioma del ticket para este cliente; null = el de impresión.</param>
    public Task<PrintOutcome> PrintInvoiceAsync(int invoiceId, bool copy, string? language = null, CancellationToken cancellationToken = default)
    {
        var doc = invoices.Get(invoiceId);
        if (doc is null)
            return Task.FromResult(new PrintOutcome("Invoice not found"));
        var profile = profiles.GetPrinter();
        // v2: factura en A4 completo en una impresora normal. Las copias (IMP-02) van como ticket, con su marca.
        if (profile.IsPagePrinter && profile.Layout == PageLayout.FullPage && invoicePages is not null && !copy)
            return SendPagesAsync(invoicePages.RenderPages(doc, language), invoicePages.Dpi, WidthMm: null, profile, doc.Code, cancellationToken);
        return SendAsync(builder.Build(doc, profiles.GetBusiness(), profile, copy, language), profile, cancellationToken);
    }

    /// <summary>CFG-03: el ticket en otro idioma, en texto, para verlo en pantalla.</summary>
    public string Preview(InvoiceDocument doc, bool copy, string? language) =>
        TextPreview.Render(builder.Build(doc, profiles.GetBusiness(), profiles.GetPrinter(), copy, language), profiles.GetPrinter());

    /// <summary>BAZ-07: ticket regalo (sin precios).</summary>
    public Task<PrintOutcome> PrintGiftAsync(int invoiceId, CancellationToken cancellationToken = default)
    {
        var doc = invoices.Get(invoiceId);
        if (doc is null)
            return Task.FromResult(new PrintOutcome("Invoice not found"));
        var profile = profiles.GetPrinter();
        return SendAsync(builder.BuildGift(doc, profiles.GetBusiness(), profile), profile, cancellationToken);
    }

    public string PreviewGift(InvoiceDocument doc) =>
        TextPreview.Render(builder.BuildGift(doc, profiles.GetBusiness(), profiles.GetPrinter()), profiles.GetPrinter());

    /// <summary>
    /// HW-03: abre el cajón portamonedas con un pulso, por la impresora de tickets (pin 2 o 5) o, en v1.1,
    /// por su propio puerto COM (cajones con disparador USB/serie).
    /// </summary>
    public Task<PrintOutcome> OpenDrawerAsync(CancellationToken cancellationToken = default)
    {
        var hardware = profiles.GetHardware();
        var profile = profiles.GetPrinter(PrinterDestination.Drawer);
        // Un cajón no se puede abrir a través de una impresora normal: necesita su propio puerto COM.
        if (profile.IsPagePrinter)
            return Task.FromResult(new PrintOutcome("La impresora no es térmica: indica el puerto COM del cajón (Impresora > Cajón)"));
        return SendAsync([new ReceiptDrawerKick(hardware.DrawerPin)], profile, cancellationToken, PrinterDestination.Drawer);
    }

    public Task<PrintOutcome> PrintTestAsync(CancellationToken cancellationToken = default)
    {
        var profile = profiles.GetPrinter();
        return SendAsync(builder.BuildTest(profiles.GetBusiness(), profile), profile, cancellationToken);
    }

    /// <summary>El ticket tal como saldrá, en texto, para verlo en pantalla.</summary>
    public string Preview(InvoiceDocument doc, bool copy) =>
        TextPreview.Render(builder.Build(doc, profiles.GetBusiness(), profiles.GetPrinter(), copy), profiles.GetPrinter());

    /// <summary>IMP-03: vista previa con los datos del negocio que aún no se han guardado.</summary>
    public string PreviewSample(BusinessProfile business, PrinterProfile printer) =>
        TextPreview.Render(builder.Build(SampleDocument(business), business, printer, copy: false), printer);

    private async Task<PrintOutcome> SendAsync(IReadOnlyList<ReceiptElement> receipt, PrinterProfile profile,
        CancellationToken cancellationToken, PrinterDestination destination = PrinterDestination.Receipt)
    {
        if (profile.IsPagePrinter)
        {
            // Ticket: la columna con su ancho real (58/80 mm); ajustado: todo el ancho del papel (etiquetas).
            var width = profile.Layout == PageLayout.FitWidth ? (double?)null : profile.PrintableWidthMm;
            return await SendPagesAsync(PageRenderer.Render(receipt, profile), PageRenderer.Dpi, width, profile, "StarSeaPOS", cancellationToken);
        }

        try
        {
            await printer.PrintAsync(EscPosEncoder.Encode(receipt, profile), destination, cancellationToken);
            return PrintOutcome.Ok;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // La venta ya está guardada: un fallo de impresora nunca debe perderla ni bloquear la caja.
            return new PrintOutcome(e.Message);
        }
    }

    private async Task<PrintOutcome> SendPagesAsync(IReadOnlyList<byte[]> pages, int dpi, double? WidthMm, PrinterProfile profile,
        string documentName, CancellationToken cancellationToken)
    {
        if (pagePrinter is null)
            return new PrintOutcome("No page printer");
        if (string.IsNullOrWhiteSpace(profile.Target))
            return new PrintOutcome("No printer selected");
        if (pages.Count == 0)
            return PrintOutcome.Ok;
        try
        {
            await pagePrinter.PrintAsync(new PagePrintJob(profile.Target.Trim(), pages, WidthMm, dpi, documentName), cancellationToken);
            return PrintOutcome.Ok;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            return new PrintOutcome(e.Message); // la venta ya está guardada
        }
    }

    private static InvoiceDocument SampleDocument(BusinessProfile business) => new(
        0, 0, InvoiceType.Simplified, "T2026-000123", DateTime.UtcNow,
        business.Name, business.Nif, business.FullAddress, null, null, null, null, "Ana",
        [
            new InvoiceDocumentLine("Taza de cerámica", 2, 3.50m, 21m, 7.00m),
            new InvoiceDocumentLine("Cuaderno A4", 1, 2.95m, 21m, 2.95m),
        ],
        [new InvoiceVatLine { Rate = 21m, Base = 8.22m, VatAmount = 1.73m, Total = 9.95m }],
        [new InvoiceDocumentPayment(PaymentMethod.Cash, 9.95m)],
        9.95m, 20m, 10.05m);
}

/// <summary>Representación en texto de un ticket (vista previa y "impresora" de fichero).</summary>
public static class TextPreview
{
    public static string Render(IEnumerable<ReceiptElement> receipt, PrinterProfile printer)
    {
        var width = printer.CharsPerLine;
        var sb = new StringBuilder();
        foreach (var element in receipt)
        {
            switch (element)
            {
                case ReceiptText t:
                    var text = t.Text.Length > width ? t.Text[..width] : t.Text;
                    sb.AppendLine(t.Align switch
                    {
                        ReceiptAlign.Center => text.PadLeft((width + text.Length) / 2),
                        ReceiptAlign.Right => text.PadLeft(width),
                        _ => text,
                    });
                    break;
                case ReceiptSeparator:
                    sb.AppendLine(new string('-', width));
                    break;
                case ReceiptBlankLine:
                    sb.AppendLine();
                    break;
                case ReceiptQr qr:
                    sb.AppendLine($"[QR: {qr.Data}]".PadLeft((width + qr.Data.Length + 6) / 2));
                    break;
                case ReceiptLogo:
                    sb.AppendLine("[LOGO]".PadLeft((width + 6) / 2));
                    break;
                case ReceiptBarcode barcode:
                    sb.AppendLine($"||| {barcode.Code} |||".PadLeft((width + barcode.Code.Length + 8) / 2));
                    break;
                case ReceiptCut:
                    sb.AppendLine(new string('✂', 1) + new string('·', width - 1));
                    break;
                case ReceiptDrawerKick:
                    sb.AppendLine("[CAJÓN]");
                    break;
            }
        }
        return sb.ToString();
    }
}
