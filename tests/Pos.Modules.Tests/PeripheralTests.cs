using System.Text;
using Pos.Core.Domain;
using Pos.Core.Hardware;
using Pos.Core.Invoicing;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.Hardware;
using Pos.Modules.Printing;

namespace Pos.Modules.Tests;

/// <summary>v1.1: compatibilidad de periféricos (impresoras, cajón, visor de cliente y lector serie).</summary>
public sealed class PeripheralTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly ProfileStore _profiles;

    public PeripheralTests() => _profiles = new ProfileStore(new SettingsStore(_db.Factory));

    public void Dispose() => _db.Dispose();

    private static byte[] Encode(PrinterProfile printer, params ReceiptElement[] elements) => EscPosEncoder.Encode(elements, printer);

    private static bool Contains(byte[] bytes, params byte[] sequence) =>
        Enumerable.Range(0, bytes.Length - sequence.Length + 1).Any(i => bytes.AsSpan(i, sequence.Length).SequenceEqual(sequence));

    // --- Tablas de caracteres ---

    [Theory]
    [InlineData(PrinterCodePage.Pc858, 19)]
    [InlineData(PrinterCodePage.Wpc1252, 16)]
    [InlineData(PrinterCodePage.Pc850, 2)]
    [InlineData(PrinterCodePage.Pc437, 0)]
    public void CodePage_SelectsTheTable(PrinterCodePage codePage, byte table)
    {
        var bytes = Encode(PrinterProfile.Default with { CodePage = codePage }, new ReceiptText("Taza"));

        Assert.Equal([0x1B, (byte)'@', 0x1B, (byte)'t', table], bytes[..5]);
    }

    [Fact]
    public void Windows1252_PrintsTheEuro()
    {
        var bytes = Encode(PrinterProfile.Default with { CodePage = PrinterCodePage.Wpc1252 }, new ReceiptText("3,50 € Ñ"));

        Assert.Contains((byte)0x80, bytes); // € en Windows-1252
        Assert.Contains((byte)0xD1, bytes); // Ñ
    }

    [Theory]
    [InlineData(PrinterCodePage.Pc850)]
    [InlineData(PrinterCodePage.Pc437)]
    public void TablesWithoutEuro_WriteEUR_AndKeepTheAccentsTheyHave(PrinterCodePage codePage)
    {
        var bytes = Encode(PrinterProfile.Default with { CodePage = codePage }, new ReceiptText("Total 3,50 € cerámica"));

        Assert.True(Contains(bytes, Encoding.ASCII.GetBytes("3,50 EUR")));
        Assert.Contains((byte)0xA0, bytes); // á existe en PC850 y en PC437
        Assert.DoesNotContain((byte)'?', bytes);
    }

    [Fact]
    public void CharactersMissingFromTheTable_LoseTheirAccent()
    {
        var bytes = EscPosEncoder.EncodeText("Ŝ€", EscPosEncoder.TextEncoding(PrinterEncoding.Western, PrinterCodePage.Pc437));

        Assert.Equal(Encoding.ASCII.GetBytes("SEUR"), bytes);
    }

    // --- Corte ---

    [Fact]
    public void Cut_PartialFullOrNone()
    {
        Assert.True(Contains(Encode(PrinterProfile.Default, new ReceiptCut()), 0x1D, (byte)'V', 66, 0));
        Assert.True(Contains(Encode(PrinterProfile.Default with { Cut = PrinterCutMode.Full }, new ReceiptCut()), 0x1D, (byte)'V', 65, 0));
        var none = Encode(PrinterProfile.Default with { Cut = PrinterCutMode.None }, new ReceiptCut());
        Assert.False(Contains(none, 0x1D, (byte)'V'));
        Assert.True(Contains(none, 0x1B, (byte)'d', 6)); // avanza para poder arrancar el papel
    }

    // --- QR ---

    [Fact]
    public void Qr_AsImage_ForPrintersWithoutTheQrCommand()
    {
        var printer = PrinterProfile.Default with { PaperWidthMm = 58, Qr = PrinterQrMode.Image };
        var url = "https://www2.agenciatributaria.gob.es/wlpl/TIKE-CONT/ValidarQR?nif=B12345674&numserie=T2026-000001&fecha=01-10-2026&importe=7.00";

        var bytes = Encode(printer, new ReceiptQr(url));

        Assert.False(Contains(bytes, 0x1D, (byte)'(', (byte)'k'));
        Assert.True(Contains(bytes, 0x1D, (byte)'v', (byte)'0', 0));
        var image = QrRaster.Render(url, printer.DotsPerLine);
        Assert.True(image.BytesPerRow * 8 <= printer.DotsPerLine);
        Assert.Equal(image.BytesPerRow * image.Height, image.Data.Length);
        Assert.True(image.Height <= printer.DotsPerLine);
    }

    [Fact]
    public void Qr_Native_ByDefault()
    {
        Assert.True(Contains(Encode(PrinterProfile.Default, new ReceiptQr("T2026-000001")), 0x1D, (byte)'(', (byte)'k'));
    }

    // --- Modelos ---

    [Fact]
    public void Models_ApplyTheirSettings()
    {
        var p58 = PrinterProfile.Default.WithModel(PrinterModel.Generic58NoCutter);
        Assert.Equal((58, PrinterCodePage.Pc437, PrinterCutMode.None, PrinterQrMode.Image), (p58.PaperWidthMm, p58.CodePage, p58.Cut, p58.Qr));

        var epson = p58.WithModel(PrinterModel.EpsonCompatible);
        Assert.Equal((PrinterCodePage.Pc858, PrinterCutMode.Partial, PrinterQrMode.Native), (epson.CodePage, epson.Cut, epson.Qr));
    }

    [Fact]
    public void Profiles_RoundTripTheNewSettings()
    {
        var printer = new PrinterProfile(PrinterConnection.Serial, "COM3", 58, PrinterEncoding.Western, AutoPrintMode.Yes, 19200,
            PrinterCodePage.Pc850, PrinterCutMode.None, PrinterQrMode.Image, SerialHandshake.DtrDsr, PrinterModel.Custom);
        var hardware = new HardwareProfile(true, true, "Hola", 5, "COM7", "COM4", PoleDisplayProtocol.Cd5220, 2400, "COM5", 115200);

        _profiles.SavePrinter(printer);
        _profiles.SaveHardware(hardware);

        Assert.Equal(printer, _profiles.GetPrinter());
        Assert.Equal(hardware, _profiles.GetHardware());
    }

    // --- Cajón ---

    [Fact]
    public async Task Drawer_Pin5_AndItsOwnPort()
    {
        var raw = new CapturingPrinter();
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "locales"));
        var print = new PrintLocalization(localizer, new RegionFormatter(localizer));
        var service = new PrintService(raw, new NoInvoices(), _profiles, new ReceiptBuilder(print), new ReportBuilder(print));

        _profiles.SaveHardware(HardwareProfile.Default with { DrawerPin = 5 });
        await service.OpenDrawerAsync();
        Assert.Equal(PrinterDestination.Drawer, raw.Destination);
        Assert.True(Contains(raw.Data!, 0x1B, (byte)'p', 1, 25, 250));
        Assert.Equal(PrinterConnection.File, _profiles.GetPrinter(PrinterDestination.Drawer).Connection); // por la impresora

        _profiles.SaveHardware(HardwareProfile.Default with { DrawerPort = "COM7" });
        var drawer = _profiles.GetPrinter(PrinterDestination.Drawer);
        Assert.Equal((PrinterConnection.Serial, "COM7"), (drawer.Connection, drawer.Target));
    }

    // --- Visor de cliente ---

    [Fact]
    public void PoleDisplay_EscPos_TwoLinesOf20()
    {
        var bytes = PoleDisplay.Encode(PoleDisplayProtocol.EscPos, PoleDisplay.Columns2("Taza de cerámica grande", "3,50"), PoleDisplay.Columns2("TOTAL", "13,90"));
        var text = Encoding.ASCII.GetString(bytes);

        Assert.Equal([0x1B, 0x40, 0x0C, 0x1F, 0x24, 1, 1], bytes[..7]);
        Assert.Contains("Taza de ceramic 3,50", text);
        Assert.Contains("TOTAL          13,90", text);
        Assert.True(Contains(bytes, 0x1F, 0x24, 1, 2));
    }

    [Fact]
    public void PoleDisplay_Cd5220()
    {
        var bytes = PoleDisplay.Encode(PoleDisplayProtocol.Cd5220, PoleDisplay.Center("Bazar"), "Total 9,95 €");
        var text = Encoding.ASCII.GetString(bytes);

        Assert.True(Contains(bytes, 0x1B, (byte)'Q', (byte)'A'));
        Assert.True(Contains(bytes, 0x1B, (byte)'Q', (byte)'B'));
        Assert.Contains("       Bazar        \r", text);
        Assert.Contains("Total 9,95 EUR      \r", text);
    }

    [Fact]
    public void PoleDisplay_WithoutPort_DoesNothing()
    {
        Assert.Same(Task.CompletedTask, new PoleDisplay(_profiles).ShowAsync("a", "b"));
    }

    // --- Lector serie ---

    [Fact]
    public void ScanBuffer_JoinsChunks_AndSplitsOnCrLfOrTab()
    {
        var buffer = new ScanBuffer();

        Assert.Empty(buffer.Append("84100"));
        Assert.Equal(["8410000000016"], buffer.Append("00000016\r"));
        Assert.Empty(buffer.Append("\n"));                       // el LF de CR+LF no da un código vacío
        Assert.Equal(["111", "222"], buffer.Append("111\n222\t"));
    }

    [Fact]
    public void SerialScanner_WithoutPort_StaysInKeyboardMode()
    {
        using var scanner = new SerialScanner(_profiles);
        var codes = new List<string>();
        scanner.Scanned += codes.Add;

        scanner.Restart();
        scanner.Receive("8410000000016\r\n");

        Assert.False(scanner.IsRunning);
        Assert.Equal(["8410000000016"], codes);
    }

    private sealed class CapturingPrinter : IRawPrinter
    {
        public byte[]? Data { get; private set; }

        public PrinterDestination? Destination { get; private set; }

        public Task PrintAsync(byte[] data, PrinterDestination destination, CancellationToken cancellationToken = default)
        {
            (Data, Destination) = (data, destination);
            return Task.CompletedTask;
        }
    }

    private sealed class NoInvoices : IInvoiceDocuments
    {
        public InvoiceDocument? Get(int invoiceId) => null;

        public InvoiceDocument? GetCurrentForSale(int saleId) => null;
    }
}
