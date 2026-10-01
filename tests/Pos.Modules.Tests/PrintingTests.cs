using Pos.Core.Domain;
using Pos.Core.Invoicing;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.Hardware;
using Pos.Modules.Printing;

namespace Pos.Modules.Tests;

/// <summary>Sección 3: tickets e impresora (IMP-01/02/03, HW-01).</summary>
public sealed class PrintingTests : IDisposable
{
    private static readonly InvoiceDocument Doc = new(
        1, 1, InvoiceType.Simplified, "T2026-000007", new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc),
        "Bazar Estrella del Mar", "B12345674", "Calle Mayor 1, 28001 Madrid", null, null, null, null, "Luis",
        [
            new InvoiceDocumentLine("Taza de cerámica", 2, 3.50m, 21m, 7.00m),
            new InvoiceDocumentLine("Libro", 1, 10.40m, 4m, 10.40m),
        ],
        [
            new InvoiceVatLine { Rate = 21m, Base = 5.79m, VatAmount = 1.21m, Total = 7.00m },
            new InvoiceVatLine { Rate = 4m, Base = 10.00m, VatAmount = 0.40m, Total = 10.40m },
        ],
        [new InvoiceDocumentPayment(PaymentMethod.Cash, 17.40m)],
        17.40m, 20m, 2.60m);

    private readonly TestDatabase _db = new();
    private readonly ProfileStore _profiles;
    private readonly ReceiptBuilder _builder;
    private readonly ReportBuilder _reports;
    private readonly string _folder = Directory.CreateTempSubdirectory("starseapos-print-").FullName;

    public PrintingTests()
    {
        _profiles = new ProfileStore(new SettingsStore(_db.Factory));
        _profiles.SaveBusiness(InvoicingTests.Business);
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "locales"));
        _builder = new ReceiptBuilder(localizer, new RegionFormatter(localizer));
        _reports = new ReportBuilder(localizer, new RegionFormatter(localizer));
    }

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private string Preview(InvoiceDocument doc, int paper = 80, bool copy = false)
    {
        var printer = PrinterProfile.Default with { PaperWidthMm = paper };
        return TextPreview.Render(_builder.Build(doc, _profiles.GetBusiness(), printer, copy), printer);
    }

    [Fact]
    public void Ticket_HasTheDataOfASimplifiedInvoice()
    {
        var text = Preview(Doc);

        Assert.Contains("Bazar Estrella del Mar", text);
        Assert.Contains("NIF: B12345674", text);
        Assert.Contains("FACTURA SIMPLIFICADA", text);
        Assert.Contains("T2026-000007", text);
        Assert.Contains("2 x Taza de cerámica", text);
        Assert.Contains("17,40", text);
        Assert.Contains("21%", text);     // desglose por tipo
        Assert.Contains("5,79", text);
        Assert.Contains("¡Gracias por su visita!", text); // pie (IMP-03)
        Assert.Contains("[QR: T2026-000007]", text);     // para buscarlo luego (FAC-06)
        Assert.DoesNotContain("COPIA", text);
    }

    [Fact]
    public void Reprint_IsMarkedAsCopy()
    {
        // IMP-02: marca "COPIA" en el ticket.
        Assert.Contains("*** COPIA ***", Preview(Doc, copy: true));
    }

    [Fact]
    public void CompleteInvoice_ShowsCustomerNetPricesAndReplacedTicket()
    {
        var complete = Doc with
        {
            Type = InvoiceType.Complete, Code = "F2026-000001", ReplacesCode = "T2026-000007",
            CustomerNif = "B12345674", CustomerName = "Papelería Pérez S.L.", CustomerAddress = "Calle Sol 5, 08001 Barcelona",
        };

        var text = Preview(complete);

        Assert.Contains("Papelería Pérez S.L.", text);
        Assert.Contains("T2026-000007", text);
        Assert.Contains("2,89", text); // 3,50 sin IVA
    }

    [Theory]
    [InlineData(58, 32)]
    [InlineData(80, 48)]
    public void Lines_FitThePaperWidth(int paper, int chars)
    {
        var lines = Preview(Doc, paper).Split(Environment.NewLine).Where(l => !l.StartsWith('✂'));

        Assert.All(lines, l => Assert.True(l.Length <= chars, $"'{l}' ({l.Length}) > {chars}"));
    }

    [Fact]
    public void EscPos_UsesPc858SoEuroAndAccentsPrint()
    {
        var bytes = EscPosEncoder.Encode(_builder.Build(Doc, _profiles.GetBusiness(), PrinterProfile.Default, false), PrinterProfile.Default);

        Assert.Equal([0x1B, (byte)'@', 0x1B, (byte)'t', 19], bytes[..5]); // reinicio + tabla PC858
        Assert.Contains((byte)0xD5, bytes); // € en PC858
        Assert.Contains((byte)0xA0, bytes); // á de "cerámica" en PC858
        Assert.True(ContainsSequence(bytes, [0x1D, (byte)'(', (byte)'k']));  // QR
        Assert.True(ContainsSequence(bytes, [0x1D, (byte)'V', 66, 0]));      // corte
    }

    [Fact]
    public void EscPos_ChineseMode()
    {
        var printer = PrinterProfile.Default with { Encoding = PrinterEncoding.Chinese };
        var doc = Doc with { Lines = [new InvoiceDocumentLine("杯子", 1, 3.50m, 21m, 3.50m)] };

        var bytes = EscPosEncoder.Encode(_builder.Build(doc, _profiles.GetBusiness(), printer, false), printer);

        Assert.True(ContainsSequence(bytes, [0x1C, (byte)'&'])); // modo chino
        Assert.True(ContainsSequence(bytes, EscPosEncoder.TextEncoding(PrinterEncoding.Chinese).GetBytes("杯子")));
    }

    [Fact]
    public async Task FilePrinter_WritesTheTicket()
    {
        // HW-01 sin impresora: la "impresora" de fichero guarda los bytes para revisarlos.
        _profiles.SavePrinter(PrinterProfile.Default with { Connection = PrinterConnection.File, Target = _folder });
        var service = new PrintService(new RawPrinter(_profiles), new FakeInvoices(Doc), _profiles, _builder, _reports);

        var outcome = await service.PrintInvoiceAsync(1, copy: false);

        Assert.True(outcome.Success, outcome.Error);
        var file = Assert.Single(Directory.GetFiles(_folder, "*.bin"));
        Assert.True(new FileInfo(file).Length > 100);
    }

    [Fact]
    public async Task PrinterFailure_IsReportedNotThrown()
    {
        _profiles.SavePrinter(PrinterProfile.Default with { Connection = PrinterConnection.Windows, Target = "Impresora que no existe" });
        var service = new PrintService(new RawPrinter(_profiles), new FakeInvoices(Doc), _profiles, _builder, _reports);

        var outcome = await service.PrintTestAsync();

        Assert.False(outcome.Success);
        Assert.NotNull(outcome.Error);
    }

    [Fact]
    public void Profiles_RoundTrip()
    {
        var printer = new PrinterProfile(PrinterConnection.Network, "192.168.1.50:9100", 58, PrinterEncoding.Chinese, AutoPrintMode.Yes);
        _profiles.SavePrinter(printer);

        Assert.Equal(printer, _profiles.GetPrinter());
        Assert.Equal(InvoicingTests.Business, _profiles.GetBusiness());
    }

    private static bool ContainsSequence(byte[] bytes, byte[] sequence) =>
        Enumerable.Range(0, bytes.Length - sequence.Length + 1).Any(i => bytes.AsSpan(i, sequence.Length).SequenceEqual(sequence));

    private sealed class FakeInvoices(InvoiceDocument doc) : IInvoiceDocuments
    {
        public InvoiceDocument? Get(int invoiceId) => doc;

        public InvoiceDocument? GetCurrentForSale(int saleId) => doc;
    }
}
