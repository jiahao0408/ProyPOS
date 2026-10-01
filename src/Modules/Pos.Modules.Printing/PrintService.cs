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

/// <summary>Imprime tickets y facturas (IMP-01, IMP-02) y la prueba de impresora (HW-01).</summary>
public sealed class PrintService(
    IRawPrinter printer,
    IInvoiceDocuments invoices,
    ProfileStore profiles,
    ReceiptBuilder builder,
    ReportBuilder reports)
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
    public Task<PrintOutcome> PrintInvoiceAsync(int invoiceId, bool copy, CancellationToken cancellationToken = default)
    {
        var doc = invoices.Get(invoiceId);
        if (doc is null)
            return Task.FromResult(new PrintOutcome("Invoice not found"));
        var profile = profiles.GetPrinter();
        return SendAsync(builder.Build(doc, profiles.GetBusiness(), profile, copy), profile, cancellationToken);
    }

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
            }
        }
        return sb.ToString();
    }
}
