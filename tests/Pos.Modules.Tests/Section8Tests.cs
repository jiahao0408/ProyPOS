using System.IO.Compression;
using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pos.Core.Domain;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.CashRegister;
using Pos.Modules.DataTransfer;
using Pos.Modules.Invoicing;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;
using Pos.Modules.Verifactu;

namespace Pos.Modules.Tests;

/// <summary>Sección 8: informe de ventas (CAJ-03), exportar facturas (FAC-05), datos (DAT-02) y registro de facturación (DAT-04).</summary>
public sealed class Section8Tests : IDisposable
{
    private static readonly DateOnly Today = new(2026, 10, 1);

    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly string _folder = Directory.CreateTempSubdirectory("starseapos-s8-").FullName;
    private readonly SalesService _sales;
    private readonly InvoiceService _invoices;
    private readonly ExportService _exports;
    private readonly BillingRecordExport _billing;
    private readonly int _ana;
    private readonly int _luis;
    private readonly Product _taza;
    private readonly Product _libro;

    public Section8Tests()
    {
        var settings = new SettingsStore(_db.Factory);
        var profiles = new ProfileStore(settings);
        profiles.SaveBusiness(InvoicingTests.Business);
        IInvoiceHook[] invoiceHooks = [new VerifactuRecorder(_clock)];
        _sales = new SalesService(_db.Factory, _clock, [new InvoiceSaleHook(profiles, invoiceHooks)]);
        _invoices = new InvoiceService(_db.Factory, profiles, _clock, invoiceHooks, settings);
        var users = new UserService(_db.Factory, _clock);
        _ana = users.CreateUser("Ana", "1111", Role.Admin).Value!.Id;
        _luis = users.CreateUser("Luis", "2222", Role.Cashier).Value!.Id;
        var catalog = new CatalogService(_db.Factory);
        _taza = catalog.SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "111", null, null)).Value!;
        _libro = catalog.SaveProduct(null, new ProductInput("Libro", 10.40m, 4m, "222", null, null)).Value!;
        new CashRegisterService(_db.Factory, _clock).Open(_ana, 100m);

        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "locales"));
        _exports = new ExportService(_db.Factory, localizer);
        _billing = new BillingRecordExport(_db.Factory, _clock);
    }

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_folder, recursive: true);
    }

    private Sale Sell(int userId, PaymentRequest payment, params (Product Product, int Quantity)[] items)
    {
        var ticket = new Ticket();
        foreach (var (product, quantity) in items)
            ticket.Add(TicketItem.FromProduct(product), quantity);
        var result = _sales.Checkout(ticket, payment, userId);
        Assert.True(result.Success, result.ErrorKey);
        _clock.Advance(TimeSpan.FromMinutes(5));
        return result.Value!.Sale;
    }

    /// <summary>Taza ×2 en efectivo (Ana), libro con tarjeta (Luis), taza en efectivo (Luis) y devolución de una taza.</summary>
    private void SampleDay()
    {
        var first = Sell(_ana, PaymentRequest.Cash(10m), (_taza, 2));
        Sell(_luis, PaymentRequest.Card(10.40m), (_libro, 1));
        Sell(_luis, PaymentRequest.Cash(5m), (_taza, 1));
        var line = _sales.GetReturnable(first.Id)[0];
        Assert.True(_sales.Return(first.Id, [new ReturnLineRequest(line.Line.Id, 1)], "Rota", PaymentMethod.Cash, _ana, "Ana").Success);
    }

    private string PathFor(string name) => Path.Combine(_folder, name);

    // --- CAJ-03 ---

    [Fact]
    public void SalesReport_ByProductPaymentAndCashier()
    {
        SampleDay();

        var report = new SalesReportService(_db.Factory).Build(Today, Today);

        Assert.Equal((3, 20.90m, 1, -3.50m, 17.40m), (report.TicketCount, report.SalesTotal, report.ReturnCount, report.ReturnsTotal, report.NetTotal));
        Assert.Equal(6.97m, report.AverageTicket);
        Assert.Equal([("Libro", 1, 10.40m), ("Taza", 2, 7.00m)], report.ByProduct.Select(l => (l.Name, l.Count, l.Total)));
        Assert.Equal([("PayCash", 3, 7.00m), ("PayCard", 1, 10.40m)], report.ByPayment.Select(l => (l.Name, l.Count, l.Total)));
        Assert.Equal([("Luis", 2, 13.90m), ("Ana", 1, 3.50m)], report.ByCashier.Select(l => (l.Name, l.Count, l.Total)));
        Assert.Equal(0, new SalesReportService(_db.Factory).Build(Today.AddDays(1), Today.AddDays(1)).TicketCount);
    }

    // --- FAC-05 ---

    [Fact]
    public void InvoiceExport_HasBaseAndVatPerRateAndTotals()
    {
        SampleDay();
        var path = PathFor("facturas.xlsx");

        var count = _exports.Export(ExportKind.Invoices, path, Today, Today);

        Assert.Equal(4, count);
        using var workbook = new XLWorkbook(path);
        var sheet = workbook.Worksheet(1);
        Assert.Equal(["Fecha", "Factura", "Tipo", "NIF del cliente", "Nombre o razón social", "Rectifica a", "Sustituida por",
                "Base 21 %", "Cuota 21 %", "Base 4 %", "Cuota 4 %", "Total"],
            Enumerable.Range(1, 12).Select(c => sheet.Cell(1, c).GetString()));
        Assert.Equal(("R2026-000001", "Rectificativa", "T2026-000001"), (sheet.Cell(5, 2).GetString(), sheet.Cell(5, 3).GetString(), sheet.Cell(5, 6).GetString()));
        Assert.Equal("TOTAL", sheet.Cell(6, 2).GetString());
        Assert.Equal(17.40, sheet.Cell(6, 12).GetDouble(), 2);                // números de verdad, se pueden sumar
        Assert.Equal(10.00, sheet.Cell(6, 10).GetDouble(), 2);                // base al 4 %
    }

    [Fact]
    public void InvoiceBook_ReplacedTicketDoesNotCountTwice()
    {
        // FAC-06: el ticket T2026-000002 (libro, 10,40) se factura después: no puede sumar dos veces.
        SampleDay();
        var ticket = _invoices.Search("T2026-000002").Single();
        Assert.True(_invoices.IssueFromTicket(ticket.Id, InvoicingTests.Customer).Success);

        var book = _exports.LoadInvoiceBook(Today, Today);

        Assert.Equal(5, book.Rows.Count);
        Assert.Equal("F2026-000001", book.Rows.Single(r => r.Code == "T2026-000002").ReplacedBy);
        Assert.Equal(17.40m, book.Total);
        Assert.Equal((10.00m, 0.40m), book.TotalFor(4m));
    }

    [Fact]
    public void InvoiceBookPdf_IsGenerated()
    {
        SampleDay();
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "locales"));
        var pdf = new InvoiceBookPdf(localizer, new RegionFormatter(localizer), _exports);

        var bytes = pdf.Render(_exports.LoadInvoiceBook(Today, Today));

        Assert.Equal("%PDF", Encoding.ASCII.GetString(bytes, 0, 4));
    }

    // --- DAT-02 ---

    [Fact]
    public void Exports_ProductsCanBeReimported_SalesHaveOneRowPerLine()
    {
        SampleDay();
        var products = PathFor("productos.csv");
        var sales = PathFor("ventas.csv");

        Assert.Equal(2, _exports.Export(ExportKind.Products, products, Today, Today));
        Assert.Equal(4, _exports.Export(ExportKind.Sales, sales, Today, Today)); // 3 ventas de una línea y 1 devolución

        var productRows = TabularFile.Read(products);
        Assert.Equal<string[]>(["Nombre", "Precio (IVA incl.)", "IVA %", "Código de barras", "Categoría"], productRows[0][..5]);
        Assert.Equal<string[]>(["Libro", "10,40", "4,00", "222"], productRows[1][..4]);
        var saleRows = TabularFile.Read(sales);
        Assert.Contains(saleRows, r => r[2] == "Devolución" && r[4] == "Taza" && r[6] == "-1" && r[10] == "-3,50");

        // Lo exportado se vuelve a importar en otra tienda.
        using var other = new TestDatabase();
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "locales"));
        var import = new ImportService(other.Factory, localizer, new RegionFormatter(localizer));
        Assert.Equal(2, import.Import(import.Preview(ImportKind.Products, products)));
    }

    // --- DAT-04 ---

    [Fact]
    public void BillingRecord_ExportsAndVerifies()
    {
        SampleDay();
        var path = PathFor("registro.zip");

        var exported = _billing.Export(path);

        Assert.Equal(4, exported.Records);
        using (var zip = ZipFile.OpenRead(path))
            Assert.Equal([BillingRecordExport.RecordsFile, BillingRecordExport.InvoicesFile, BillingRecordExport.EventsFile,
                    BillingRecordExport.ReadmeFile, BillingRecordExport.ManifestFile],
                zip.Entries.Select(e => e.FullName));
        var check = _billing.Verify(path);
        Assert.True(check.IsValid, $"{check.ErrorKey} {check.Detail}");
        Assert.Equal((4, exported.FinalHash), (check.Records, check.FinalHash));
    }

    [Fact]
    public void BillingRecord_ChangingAFileIsDetected()
    {
        SampleDay();
        var path = PathFor("registro.zip");
        _billing.Export(path);

        Rewrite(path, BillingRecordExport.InvoicesFile, text => text.Replace("10.40", "1.40"));

        Assert.Equal(("ErrorExportFileChanged", BillingRecordExport.InvoicesFile), (_billing.Verify(path).ErrorKey, _billing.Verify(path).Detail));
    }

    [Fact]
    public void BillingRecord_ChangingAnAmountEvenWithANewManifest_IsDetected()
    {
        SampleDay();
        var path = PathFor("registro.zip");
        _billing.Export(path);

        // Se cambia el importe del ticket 2 en los dos ficheros y se rehace el manifiesto: la huella ya no cuadra.
        Rewrite(path, BillingRecordExport.RecordsFile, text => text.Replace(";0.40;10.40;", ";0.40;1.40;"));
        Rewrite(path, BillingRecordExport.InvoicesFile, text => text.Replace("10.40", "1.40"));
        RebuildManifest(path);

        var check = _billing.Verify(path);
        Assert.Equal("ErrorExportHashMismatch", check.ErrorKey);
        Assert.Equal("2 · T2026-000002", check.Detail);
    }

    [Fact]
    public void BillingRecord_RecomputingTheWholeChain_DoesNotMatchTheDatabase()
    {
        SampleDay();
        var path = PathFor("registro.zip");
        _billing.Export(path);

        // Falsificación "perfecta": se cambia un importe y se recalculan todas las huellas y el manifiesto.
        string? last = null;
        Rewrite(path, BillingRecordExport.RecordsFile, text =>
        {
            var rows = TabularFile.ParseCsv(text, ';').ToList();
            for (var i = 1; i < rows.Count; i++)
            {
                var r = rows[i];
                if (r[4] == "T2026-000002")
                    r[8] = "1.40";
                r[9] = last ?? "";
                r[11] = Pos.Core.Verifactu.VerifactuHash.ForAlta(r[2], r[4], r[5], r[6], decimal.Parse(r[7], System.Globalization.CultureInfo.InvariantCulture),
                    decimal.Parse(r[8], System.Globalization.CultureInfo.InvariantCulture), r[9], r[10]);
                last = r[11];
            }
            return TabularFile.ToCsv(rows[0], rows.Skip(1));
        });
        Rewrite(path, BillingRecordExport.InvoicesFile, text => text.Replace("10.40", "1.40"));
        Rewrite(path, BillingRecordExport.ManifestFile, text => string.Join('\n', text.Split('\n')
            .Select(l => l.StartsWith("HuellaFinal:", StringComparison.Ordinal) ? $"HuellaFinal: {last}" : l)));
        RebuildManifest(path);

        var check = _billing.Verify(path);
        Assert.Equal(("ErrorExportDiffersFromDatabase", "2 · T2026-000002"), (check.ErrorKey, check.Detail));
    }

    private static void Rewrite(string zipPath, string entryName, Func<string, string> change)
    {
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
        var entry = zip.GetEntry(entryName)!;
        string text;
        using (var reader = new StreamReader(entry.Open()))
            text = reader.ReadToEnd();
        entry.Delete();
        using var writer = new StreamWriter(zip.CreateEntry(entryName).Open(), new UTF8Encoding(false));
        writer.Write(change(text));
    }

    private static void RebuildManifest(string zipPath)
    {
        Dictionary<string, string> files;
        using (var zip = ZipFile.OpenRead(zipPath))
            files = zip.Entries.ToDictionary(e => e.FullName, e => new StreamReader(e.Open()).ReadToEnd());
        var lines = files[BillingRecordExport.ManifestFile].Split('\n').Select(l => l.TrimEnd('\r'))
            .Select(l => l.StartsWith("SHA256 ", StringComparison.Ordinal)
                ? $"SHA256 {Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(files[l.Split(' ')[2]])))} {l.Split(' ')[2]}"
                : l);
        Rewrite(zipPath, BillingRecordExport.ManifestFile, _ => string.Join('\n', lines));
    }
}
