using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pos.Core.Domain;
using Pos.Core.Invoicing;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.CashRegister;
using Pos.Modules.Invoicing;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;

namespace Pos.Modules.Tests;

/// <summary>Sección 3: facturas (FAC-01, FAC-02, FAC-06).</summary>
public sealed class InvoicingTests : IDisposable
{
    internal static readonly BusinessProfile Business =
        new("Bazar Estrella del Mar", "B12345674", "Calle Mayor 1", "28001", "Madrid", "910000000", "¡Gracias por su visita!", null);

    internal static readonly InvoiceCustomer Customer =
        new("B12345674", "Papelería Pérez S.L.", "Calle Sol 5", "08001", "Barcelona");

    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly ProfileStore _profiles;
    private readonly SalesService _sales;
    private readonly InvoiceService _invoices;
    private readonly int _userId;
    private readonly Product _taza;
    private readonly Product _libro;

    public InvoicingTests()
    {
        _profiles = new ProfileStore(new SettingsStore(_db.Factory));
        _profiles.SaveBusiness(Business);
        _sales = new SalesService(_db.Factory, _clock, [new InvoiceSaleHook(_profiles)]);
        _invoices = new InvoiceService(_db.Factory, _profiles, _clock);
        _userId = new UserService(_db.Factory, _clock).CreateUser("Luis", "2222", Role.Cashier).Value!.Id;
        var catalog = new CatalogService(_db.Factory);
        _taza = catalog.SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "111", null, null)).Value!;
        _libro = catalog.SaveProduct(null, new ProductInput("Libro", 10.40m, 4m, "222", null, null)).Value!;
        new CashRegisterService(_db.Factory, _clock).Open(_userId, 100m);
    }

    public void Dispose() => _db.Dispose();

    private Sale Sell(InvoiceCustomer? customer = null)
    {
        var ticket = new Ticket();
        ticket.Add(TicketItem.FromProduct(_taza), 2);
        ticket.Add(TicketItem.FromProduct(_libro));
        var result = _sales.Checkout(ticket, PaymentRequest.Cash(20m), _userId, customer);
        Assert.True(result.Success, result.ErrorKey);
        return result.Value!.Sale;
    }

    [Fact]
    public void EverySale_GetsASimplifiedInvoiceWithVatBreakdown()
    {
        // FAC-01: serie y número correlativos; desglose de base, IVA y total.
        var sale = Sell();

        var doc = _invoices.GetCurrentForSale(sale.Id)!;
        Assert.Equal(InvoiceType.Simplified, doc.Type);
        Assert.Equal("T2026-000001", doc.Code);
        Assert.Equal(17.40m, doc.Total);
        Assert.Equal(("Bazar Estrella del Mar", "B12345674", "Calle Mayor 1, 28001 Madrid"), (doc.IssuerName, doc.IssuerNif, doc.IssuerAddress));
        Assert.Collection(doc.VatLines,
            v => Assert.Equal((21m, 5.79m, 1.21m, 7.00m), (v.Rate, v.Base, v.VatAmount, v.Total)),
            v => Assert.Equal((4m, 10.00m, 0.40m, 10.40m), (v.Rate, v.Base, v.VatAmount, v.Total)));
        Assert.Equal("Luis", doc.CashierName);
    }

    [Fact]
    public void Numbers_AreCorrelativeWithoutGaps()
    {
        var codes = Enumerable.Range(0, 5).Select(_ => _invoices.GetCurrentForSale(Sell().Id)!.Code).ToList();

        Assert.Equal(["T2026-000001", "T2026-000002", "T2026-000003", "T2026-000004", "T2026-000005"], codes);
    }

    [Fact]
    public void FailedCheckout_DoesNotConsumeANumber()
    {
        Sell();
        var empty = _sales.Checkout(new Ticket(), PaymentRequest.Cash(1m), _userId);
        var bad = _sales.Checkout(TicketOf(_taza), PaymentRequest.Cash(1m), _userId);

        Assert.False(empty.Success);
        Assert.False(bad.Success);
        Assert.Equal("T2026-000002", _invoices.GetCurrentForSale(Sell().Id)!.Code);
    }

    [Fact]
    public void Series_ChangesWithTheYear()
    {
        Sell();
        _clock.SetUtcNow(new DateTimeOffset(2027, 1, 2, 9, 0, 0, TimeSpan.Zero));

        Assert.Equal("T2027-000001", _invoices.GetCurrentForSale(Sell().Id)!.Code);
    }

    [Fact]
    public void Checkout_WithoutBusinessFiscalData_IsBlocked()
    {
        _profiles.SaveBusiness(Business with { Nif = "" });

        var result = _sales.Checkout(TicketOf(_taza), PaymentRequest.Cash(5m), _userId);

        Assert.Equal("ErrorBusinessNif", result.ErrorKey);
    }

    [Fact]
    public void CompleteInvoice_AtCheckout()
    {
        // FAC-02: factura completa con NIF, razón social y dirección.
        var sale = Sell(Customer);

        var doc = _invoices.GetCurrentForSale(sale.Id)!;
        Assert.Equal(InvoiceType.Complete, doc.Type);
        Assert.Equal("F2026-000001", doc.Code);
        Assert.Equal(("B12345674", "Papelería Pérez S.L.", "Calle Sol 5, 08001 Barcelona"),
            (doc.CustomerNif, doc.CustomerName, doc.CustomerAddress));
        Assert.Equal("Papelería Pérez S.L.", _invoices.FindCustomer("b-12345674")!.Name); // se recuerda el cliente
    }

    [Theory]
    [InlineData("12345678A", "Cliente", "Calle 1", "ErrorCustomerNif")]
    [InlineData("12345678Z", "", "Calle 1", "ErrorCustomerNameRequired")]
    [InlineData("12345678Z", "Cliente", "", "ErrorCustomerAddressRequired")]
    public void CompleteInvoice_ValidatesCustomer(string nif, string name, string address, string expectedError)
    {
        var result = _sales.Checkout(TicketOf(_taza), PaymentRequest.Cash(5m), _userId,
            new InvoiceCustomer(nif, name, address, "", ""));

        Assert.Equal(expectedError, result.ErrorKey);
    }

    [Fact]
    public void InvoiceFromTicket_ReplacesTheSimplifiedOne()
    {
        // FAC-06: factura completa en sustitución de la simplificada, con referencia a ella.
        var sale = Sell();
        var ticket = _invoices.GetCurrentForSale(sale.Id)!;

        var result = _invoices.IssueFromTicket(ticket.InvoiceId, Customer);

        Assert.True(result.Success, result.ErrorKey);
        var complete = result.Value!;
        Assert.Equal(("F2026-000001", "T2026-000001"), (complete.Code, complete.ReplacesCode));
        Assert.Equal(ticket.Total, complete.Total);
        Assert.Equal(ticket.Lines, complete.Lines);
        Assert.Equal(complete.Code, _invoices.GetCurrentForSale(sale.Id)!.Code);
        Assert.Equal("T2026-000001", _invoices.Get(ticket.InvoiceId)!.Code); // la original sigue existiendo
        var summary = Assert.Single(_invoices.Search("T2026-000001"));
        Assert.True(summary.IsReplaced);
        Assert.Equal("F2026-000001", summary.ReplacedByCode);
    }

    [Fact]
    public void InvoiceFromTicket_OnlyOnce()
    {
        var ticket = _invoices.GetCurrentForSale(Sell().Id)!;
        _invoices.IssueFromTicket(ticket.InvoiceId, Customer);

        Assert.Equal("ErrorTicketAlreadyInvoiced", _invoices.IssueFromTicket(ticket.InvoiceId, Customer).ErrorKey);
    }

    [Fact]
    public void InvoiceFromTicket_RejectsCompleteInvoices()
    {
        var complete = _invoices.GetCurrentForSale(Sell(Customer).Id)!;

        Assert.Equal("ErrorInvoiceNotSimplified", _invoices.IssueFromTicket(complete.InvoiceId, Customer).ErrorKey);
    }

    [Theory]
    [InlineData("T2026-000002")]
    [InlineData("t2026-000002")]
    [InlineData("2")]
    [InlineData("https://ejemplo/?num=T2026-000002&x=1")] // texto leído de un QR
    public void Search_ByCodeNumberOrQr(string text)
    {
        Sell();
        Sell();

        Assert.Equal("T2026-000002", Assert.Single(_invoices.Search(text)).Code);
    }

    [Fact]
    public void ListByDay_ReturnsThatDaysInvoices()
    {
        Sell();
        _clock.Advance(TimeSpan.FromDays(1));
        Sell();

        var today = DateOnly.FromDateTime(_clock.GetLocalNow().DateTime);
        Assert.Equal("T2026-000002", Assert.Single(_invoices.ListByDay(today)).Code);
    }

    [Fact]
    public void Invoices_CannotBeModifiedOrDeleted()
    {
        Sell();
        using var db = _db.Factory.CreateDbContext();

        Assert.Throws<SqliteException>(() => db.Invoices.ExecuteDelete());
        Assert.Throws<SqliteException>(() => db.Invoices.ExecuteUpdate(s => s.SetProperty(i => i.Total, 0m)));
        Assert.Throws<SqliteException>(() => db.InvoiceVatLines.ExecuteDelete());
    }

    [Fact]
    public void Pdf_IsGenerated()
    {
        var doc = _invoices.GetCurrentForSale(Sell(Customer).Id)!;
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "locales"));
        var pdf = new InvoicePdf(localizer, new RegionFormatter(localizer)).Render(doc);

        Assert.Equal("%PDF", Encoding.ASCII.GetString(pdf, 0, 4));
        Assert.True(pdf.Length > 1000);
    }

    private static Ticket TicketOf(Product product)
    {
        var ticket = new Ticket();
        ticket.Add(TicketItem.FromProduct(product));
        return ticket;
    }
}
