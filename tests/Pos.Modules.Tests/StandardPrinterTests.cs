using System.Drawing;
using Pos.Core.Domain;
using Pos.Core.Hardware;
using Pos.Core.Invoicing;
using Pos.Core.Printing;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.Invoicing;
using Pos.Modules.Printing;

namespace Pos.Modules.Tests;

/// <summary>v2: impresoras normales de Windows (HP, Brother, Canon…) además de las térmicas.</summary>
public sealed class StandardPrinterTests : IDisposable
{
    private static readonly InvoiceDocument Doc = new(
        1, 1, InvoiceType.Simplified, "T2026-000007", new DateTime(2026, 10, 1, 9, 30, 0, DateTimeKind.Utc),
        "Bazar Estrella del Mar", "B12345674", "Calle Mayor 1, 28001 Madrid", null, null, null, null, "Luis",
        [new InvoiceDocumentLine("Taza de cerámica", 2, 3.50m, 21m, 7.00m)],
        [new InvoiceVatLine { Rate = 21m, Base = 5.79m, VatAmount = 1.21m, Total = 7.00m }],
        [new InvoiceDocumentPayment(PaymentMethod.Cash, 7.00m)],
        7.00m, 10m, 3.00m,
        QrUrl: "https://www2.agenciatributaria.gob.es/wlpl/TIKE-CONT/ValidarQR?nif=B12345674&numserie=T2026-000007&fecha=01-10-2026&importe=7.00",
        VerifactuMode: true);

    private static readonly PrinterProfile Laser = PrinterProfile.Default with
    {
        Connection = PrinterConnection.WindowsDriver,
        Target = "HP LaserJet",
        Layout = PageLayout.Ticket,
    };

    private readonly TestDatabase _db = new();
    private readonly ProfileStore _profiles;
    private readonly PrintLocalization _print;
    private readonly ReceiptBuilder _builder;
    private readonly ReportBuilder _reports;
    private readonly FakePagePrinter _pages = new();
    private readonly FakeRaw _raw = new();

    public StandardPrinterTests()
    {
        _profiles = new ProfileStore(new SettingsStore(_db.Factory));
        _profiles.SaveBusiness(InvoicingTests.Business);
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "locales"));
        _print = new PrintLocalization(localizer, new RegionFormatter(localizer));
        _builder = new ReceiptBuilder(_print);
        _reports = new ReportBuilder(_print);
    }

    public void Dispose() => _db.Dispose();

    private PrintService Service(IInvoicePageRenderer? invoicePages = null) =>
        new(_raw, new OneInvoice(), _profiles, _builder, _reports, _pages, invoicePages);

    private static Bitmap Decode(byte[] png) => new(new MemoryStream(png));

    private static int DarkPixels(Bitmap bitmap)
    {
        var dark = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
            for (var x = 0; x < bitmap.Width; x += 2)
                if (bitmap.GetPixel(x, y).GetBrightness() < 0.5f)
                    dark++;
        return dark;
    }

    // --- Dibujo de las páginas ---

    [Theory]
    [InlineData(80, 850)]
    [InlineData(58, 567)]
    public void Ticket_IsDrawnAtItsRealWidth(int paper, int widthPixels)
    {
        var printer = Laser with { PaperWidthMm = paper };

        var page = Assert.Single(PageRenderer.Render(_builder.Build(Doc, _profiles.GetBusiness(), printer, copy: false), printer));

        using var bitmap = Decode(page);
        Assert.Equal(widthPixels, bitmap.Width);           // 72 / 48 mm imprimibles a 300 ppp
        Assert.True(bitmap.Height > bitmap.Width);          // un ticket es alargado
        Assert.True(DarkPixels(bitmap) > 1000);             // texto, QR…
    }

    [Fact]
    public void Labels_OnePagePerLabel_WithBarcodeDrawn()
    {
        var labels = new[] { new LabelItem("Taza", 3.50m, "8410000000016", 2), new LabelItem("Llavero", 1.95m, "SS-0001", 1) };

        var pages = PageRenderer.Render(_reports.BuildLabels(labels, Laser), Laser);

        Assert.Equal(3, pages.Count);
        using var first = Decode(pages[0]);
        using var third = Decode(pages[2]);
        Assert.True(DarkPixels(first) > 500);
        Assert.True(DarkPixels(third) > 500);               // CODE128 para el código interno
    }

    [Fact]
    public void DrawerKick_IsNotDrawn()
    {
        Assert.Empty(PageRenderer.Render([new ReceiptDrawerKick(), new ReceiptCut()], Laser));
    }

    // --- Envío ---

    [Fact]
    public async Task StandardPrinter_ReceivesPages_NotEscPos()
    {
        _profiles.SavePrinter(Laser);

        var outcome = await Service().PrintInvoiceAsync(1, copy: false);

        Assert.True(outcome.Success, outcome.Error);
        Assert.Null(_raw.Data);
        var job = Assert.Single(_pages.Jobs);
        Assert.Equal(("HP LaserJet", 72.0, PageRenderer.Dpi), (job.PrinterName, job.WidthMm!.Value, job.Dpi));
        Assert.Single(job.PngPages);
    }

    [Fact]
    public async Task FullPage_PrintsTheInvoiceInA4_AndCopiesAsTicket()
    {
        _profiles.SavePrinter(Laser with { Layout = PageLayout.FullPage });
        var a4 = new FakeInvoicePages();

        await Service(a4).PrintInvoiceAsync(1, copy: false);
        await Service(a4).PrintInvoiceAsync(1, copy: true);   // IMP-02: la copia lleva su marca "COPIA" (ticket)

        Assert.Equal((null, 200), (_pages.Jobs[0].WidthMm, _pages.Jobs[0].Dpi));
        Assert.Same(a4.Page, _pages.Jobs[0].PngPages[0]);
        Assert.Equal(PageRenderer.Dpi, _pages.Jobs[1].Dpi);
    }

    [Fact]
    public async Task FitWidth_UsesTheWholePaper_ForLabelPrinters()
    {
        _profiles.SavePrinter(Laser with { Layout = PageLayout.FitWidth, Target = "Brother QL-800" });

        await Service().PrintTestAsync();

        Assert.Null(Assert.Single(_pages.Jobs).WidthMm);
    }

    [Fact]
    public async Task Drawer_CannotGoThroughAStandardPrinter()
    {
        _profiles.SavePrinter(Laser);

        var outcome = await Service().OpenDrawerAsync();

        Assert.False(outcome.Success);
        Assert.Contains("COM", outcome.Error);
        Assert.Empty(_pages.Jobs);
    }

    [Fact]
    public async Task ThermalPrinters_StillGetEscPos()
    {
        _profiles.SavePrinter(PrinterProfile.Default with { Connection = PrinterConnection.Network, Target = "192.168.1.50" });

        await Service().PrintInvoiceAsync(1, copy: false);

        Assert.NotNull(_raw.Data);
        Assert.Empty(_pages.Jobs);
    }

    [Fact]
    public async Task PrinterFailure_IsReportedNotThrown()
    {
        _profiles.SavePrinter(Laser);
        _pages.Fail = true;

        var outcome = await Service().PrintInvoiceAsync(1, copy: false);

        Assert.Equal("Printer 'HP LaserJet' not found", outcome.Error);
    }

    [Fact]
    public void Layout_IsSaved()
    {
        _profiles.SavePrinter(Laser with { Layout = PageLayout.FitWidth });

        Assert.Equal(PageLayout.FitWidth, _profiles.GetPrinter().Layout);
        Assert.True(_profiles.GetPrinter().IsPagePrinter);
    }

    /// <summary>Con STARSEAPOS_SCREENSHOTS, guarda las páginas para revisarlas a ojo.</summary>
    [Fact]
    public void Pages_SavedForReview()
    {
        if (Environment.GetEnvironmentVariable("STARSEAPOS_SCREENSHOTS") is not { Length: > 0 } folder)
            return;
        var ticket = PageRenderer.Render(_builder.Build(Doc, _profiles.GetBusiness(), Laser, copy: false), Laser);
        File.WriteAllBytes(Path.Combine(folder, "v2-ticket-pagina.png"), ticket[0]);
        var label = PageRenderer.Render(_reports.BuildLabels([new LabelItem("Taza de cerámica", 3.50m, "8410000000016", 1)], Laser with { PaperWidthMm = 58 }), Laser with { PaperWidthMm = 58 });
        File.WriteAllBytes(Path.Combine(folder, "v2-etiqueta.png"), label[0]);
        File.WriteAllBytes(Path.Combine(folder, "v2-factura-a4.png"), new InvoicePdf(_print).RenderPages(Doc)[0]);
    }

    // --- Factura A4 ---

    [Fact]
    public void InvoicePdf_RendersA4Pages()
    {
        var pages = new InvoicePdf(_print).RenderPages(Doc, "en");

        using var page = Decode(Assert.Single(pages));
        Assert.InRange(page.Height / (double)page.Width, 1.40, 1.43); // proporción A4
        Assert.InRange(page.Width, 1600, 1700);                       // 210 mm a 200 ppp
    }

    private sealed class FakePagePrinter : IPagePrinter
    {
        public List<PagePrintJob> Jobs { get; } = [];

        public bool Fail { get; set; }

        public Task PrintAsync(PagePrintJob job, CancellationToken cancellationToken = default)
        {
            if (Fail)
                throw new InvalidOperationException($"Printer '{job.PrinterName}' not found");
            Jobs.Add(job);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRaw : IRawPrinter
    {
        public byte[]? Data { get; private set; }

        public Task PrintAsync(byte[] data, PrinterDestination destination, CancellationToken cancellationToken = default)
        {
            Data = data;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInvoicePages : IInvoicePageRenderer
    {
        public byte[] Page { get; } = [1, 2, 3];

        public int Dpi => 200;

        public IReadOnlyList<byte[]> RenderPages(InvoiceDocument doc, string? language = null) => [Page];
    }

    private sealed class OneInvoice : IInvoiceDocuments
    {
        public InvoiceDocument? Get(int invoiceId) => Doc;

        public InvoiceDocument? GetCurrentForSale(int saleId) => Doc;
    }
}
