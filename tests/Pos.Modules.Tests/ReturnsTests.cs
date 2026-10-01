using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pos.Core.Domain;
using Pos.Data;
using Pos.Modules.CashRegister;
using Pos.Modules.Inventory;
using Pos.Modules.Invoicing;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;
using Pos.Modules.Verifactu;

namespace Pos.Modules.Tests;

/// <summary>Sección 6: descuentos (VEN-05), devoluciones (VEN-06), rectificativas (FAC-03), cambios (BAZ-07) y auditoría (USR-03).</summary>
public sealed class ReturnsTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly SettingsStore _settings;
    private readonly SalesService _sales;
    private readonly InvoiceService _invoices;
    private readonly StockService _stock;
    private readonly CashRegisterService _cash;
    private readonly int _cashierId;
    private readonly int _adminId;
    private readonly Product _taza;
    private readonly Product _plato;

    public ReturnsTests()
    {
        _settings = new SettingsStore(_db.Factory);
        var profiles = new ProfileStore(_settings);
        profiles.SaveBusiness(InvoicingTests.Business);
        IInvoiceHook[] invoiceHooks = [new VerifactuRecorder(_clock)];
        _sales = new SalesService(_db.Factory, _clock, [new InvoiceSaleHook(profiles, invoiceHooks)]);
        _invoices = new InvoiceService(_db.Factory, profiles, _clock, invoiceHooks, _settings);
        _stock = new StockService(_db.Factory);
        _cash = new CashRegisterService(_db.Factory, _clock);
        var users = new UserService(_db.Factory, _clock);
        _cashierId = users.CreateUser("Luis", "2222", Role.Cashier).Value!.Id;
        _adminId = users.CreateUser("Ana", "1111", Role.Admin).Value!.Id;
        var catalog = new CatalogService(_db.Factory);
        _taza = catalog.SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "111", null, null)).Value!;
        _plato = catalog.SaveProduct(null, new ProductInput("Plato", 5.00m, 21m, "222", null, null)).Value!;
        _cash.Open(_adminId, 100m);
    }

    public void Dispose() => _db.Dispose();

    private Ticket TicketOf(params (Product Product, int Quantity)[] items)
    {
        var ticket = new Ticket();
        foreach (var (product, quantity) in items)
            ticket.Add(TicketItem.FromProduct(product), quantity);
        return ticket;
    }

    private Sale Sell(Ticket ticket, Pos.Core.Invoicing.InvoiceCustomer? customer = null)
    {
        var result = _sales.Checkout(ticket, PaymentRequest.Cash(100m), _adminId, customer);
        Assert.True(result.Success, result.ErrorKey);
        return result.Value!.Sale;
    }

    private List<AuditEntry> Audit()
    {
        using var db = _db.Factory.CreateDbContext();
        return db.AuditEntries.AsNoTracking().OrderBy(a => a.Id).ToList();
    }

    // --- VEN-05 ---

    [Fact]
    public void Discounts_LineAndTotal()
    {
        var ticket = TicketOf((_taza, 2), (_plato, 1)); // 7,00 + 5,00
        ticket.SetLineDiscount(ticket.Lines[0], percent: 10m, amount: null);  // 7,00 → 6,30
        ticket.SetLineDiscount(ticket.Lines[1], percent: null, amount: 1m);   // 5,00 → 4,00
        Assert.Equal(10.30m, ticket.Total);

        ticket.SetDiscountPercent(10m);                                       // 6,30 → 5,67 ; 4,00 → 3,60
        Assert.Equal(9.27m, ticket.Total);
        Assert.Equal(2.73m, ticket.Discount);
        Assert.Equal(28m, ticket.MaxEffectiveDiscountPercent);               // plato: 1,40 / 5,00
    }

    [Fact]
    public void Discount_AboveCashierLimit_NeedsAdmin()
    {
        // VEN-05: el cajero solo aplica descuentos hasta el límite que fija el admin.
        _settings.Set(SalesSettingKeys.MaxCashierDiscount, "15");
        var ticket = TicketOf((_taza, 1));
        ticket.SetDiscountPercent(20m);

        Assert.Equal("ErrorDiscountLimit", _sales.Checkout(ticket, PaymentRequest.Cash(10m), _cashierId).ErrorKey);
        Assert.True(_sales.Checkout(ticket, PaymentRequest.Cash(10m), _cashierId, discountAuthorizedBy: "Ana").Success);

        var entry = Assert.Single(Audit());
        Assert.Equal((AuditActions.Discount, "Luis", "Ana"), (entry.Action, entry.UserName, entry.AuthorizedBy));
    }

    [Fact]
    public void Discount_WithinLimit_IsSavedOnTheLines()
    {
        var ticket = TicketOf((_taza, 2));
        ticket.SetDiscountPercent(10m);

        var sale = _sales.GetSale(_sales.Checkout(ticket, PaymentRequest.Cash(10m), _cashierId).Value!.Sale.Id)!;

        Assert.Equal((6.30m, 0.70m, 10m), (sale.Total, sale.Lines[0].Discount, sale.DiscountPercent));
        var invoice = _invoices.GetCurrentForSale(sale.Id)!;
        Assert.Equal(6.30m, invoice.VatLines.Single().Total); // la factura recoge el importe con descuento
    }

    // --- VEN-06 / FAC-03 ---

    [Fact]
    public void Return_RestocksRefundsAndIssuesRectificative()
    {
        var sale = Sell(TicketOf((_taza, 3)));
        var line = Assert.Single(_sales.GetReturnable(sale.Id));

        var result = _sales.Return(sale.Id, [new ReturnLineRequest(line.Line.Id, 2)], "Taza rota", PaymentMethod.Cash, _cashierId, authorizedBy: "Ana");

        Assert.True(result.Success, result.ErrorKey);
        var returned = _sales.GetSale(result.Value!.Id)!;
        Assert.Equal((SaleKind.Return, -7.00m, -2, "Taza rota"), (returned.Kind, returned.Total, returned.Lines[0].Quantity, returned.Reason));
        Assert.Equal((PaymentMethod.Cash, -7.00m), (returned.Payments[0].Method, returned.Payments[0].Amount));
        Assert.Equal(-1, _stock.GetStock(_taza.Id)); // -3 vendidas + 2 devueltas (INV-01)
        Assert.Equal(1, Assert.Single(_sales.GetReturnable(sale.Id)).Returnable);

        var rectificative = _invoices.GetCurrentForSale(returned.Id)!;
        Assert.Equal((InvoiceType.Rectificative, "R2026-000001", -7.00m), (rectificative.Type, rectificative.Code, rectificative.Total));
        Assert.Equal("T2026-000001", rectificative.RectifiesCode);
        Assert.Equal(-1.21m, rectificative.VatLines.Single().VatAmount);
        Assert.Equal("T2026-000001", _invoices.GetCurrentForSale(sale.Id)!.Code); // la original sigue igual

        var audit = Assert.Single(Audit());
        Assert.Equal((AuditActions.Return, "Ana"), (audit.Action, audit.AuthorizedBy));
        Assert.Contains("Taza rota", audit.Details);
    }

    [Fact]
    public void Return_ReducesExpectedCashInTheRegister()
    {
        Sell(TicketOf((_plato, 2)));
        var sale = Sell(TicketOf((_taza, 1)));
        _sales.Return(sale.Id, [new ReturnLineRequest(_sales.GetReturnable(sale.Id)[0].Line.Id, 1)], "No le gusta", PaymentMethod.Cash, _adminId);

        var summary = _cash.GetSummary(_cash.GetOpenSession()!.Id)!;

        Assert.Equal(110.00m, summary.ExpectedCash); // 100 + 10 + 3,50 - 3,50
    }

    [Theory]
    [InlineData("", 1, "ErrorReasonRequired")]
    [InlineData("Rota", 0, "ErrorNothingToReturn")]
    [InlineData("Rota", 4, "ErrorReturnTooMany")]
    public void Return_Validates(string reason, int quantity, string expectedError)
    {
        var sale = Sell(TicketOf((_taza, 3)));
        var lineId = _sales.GetReturnable(sale.Id)[0].Line.Id;

        Assert.Equal(expectedError, _sales.Return(sale.Id, [new ReturnLineRequest(lineId, quantity)], reason, PaymentMethod.Cash, _adminId).ErrorKey);
    }

    [Fact]
    public void Return_CannotExceedWhatWasSoldOverSeveralReturns()
    {
        var sale = Sell(TicketOf((_taza, 2)));
        var lineId = _sales.GetReturnable(sale.Id)[0].Line.Id;
        _sales.Return(sale.Id, [new ReturnLineRequest(lineId, 1)], "1", PaymentMethod.Cash, _adminId);
        _sales.Return(sale.Id, [new ReturnLineRequest(lineId, 1)], "2", PaymentMethod.Cash, _adminId);

        Assert.Equal("ErrorReturnTooMany", _sales.Return(sale.Id, [new ReturnLineRequest(lineId, 1)], "3", PaymentMethod.Cash, _adminId).ErrorKey);
    }

    [Fact]
    public void Return_WithDiscount_RefundsWhatWasPaidExactly()
    {
        var ticket = TicketOf((_taza, 3)); // 10,50
        ticket.SetLineDiscount(ticket.Lines[0], percent: null, amount: 1m); // 9,50 → 3,1666… por unidad
        var sale = Sell(ticket);
        var lineId = _sales.GetReturnable(sale.Id)[0].Line.Id;

        var refunds = Enumerable.Range(0, 3)
            .Select(_ => _sales.Return(sale.Id, [new ReturnLineRequest(lineId, 1)], "x", PaymentMethod.Cash, _adminId).Value!.Total)
            .ToList();

        Assert.Equal([-3.17m, -3.17m, -3.16m], refunds); // la última ajusta: la suma es justo lo cobrado
        Assert.Equal(-9.50m, refunds.Sum());
    }

    [Fact]
    public void Return_OfCompleteInvoice_KeepsTheCustomer()
    {
        var sale = Sell(TicketOf((_taza, 1)), InvoicingTests.Customer);
        var result = _sales.Return(sale.Id, [new ReturnLineRequest(_sales.GetReturnable(sale.Id)[0].Line.Id, 1)], "x", PaymentMethod.Card, _adminId);

        var rectificative = _invoices.GetCurrentForSale(result.Value!.Id)!;
        Assert.Equal(("Papelería Pérez S.L.", "F2026-000001"), (rectificative.CustomerName, rectificative.RectifiesCode));
    }

    [Fact]
    public void Verifactu_RectificativesAreR5OrR1WithNegativeAmounts()
    {
        var ticket = Sell(TicketOf((_taza, 1)));
        var complete = Sell(TicketOf((_taza, 1)), InvoicingTests.Customer);
        _sales.Return(ticket.Id, [new ReturnLineRequest(_sales.GetReturnable(ticket.Id)[0].Line.Id, 1)], "x", PaymentMethod.Cash, _adminId);
        _sales.Return(complete.Id, [new ReturnLineRequest(_sales.GetReturnable(complete.Id)[0].Line.Id, 1)], "x", PaymentMethod.Cash, _adminId);

        using var db = _db.Factory.CreateDbContext();
        var records = db.VerifactuRecords.AsNoTracking().OrderBy(r => r.Id).ToList();
        Assert.Equal(["F2", "F1", "R5", "R1"], records.Select(r => r.InvoiceType));
        Assert.Equal((-3.50m, -0.61m), (records[2].Total, records[2].TotalVat));
        Assert.Equal(records[2].Hash, records[3].PreviousHash);
    }

    // --- BAZ-07 ---

    [Fact]
    public void Exchange_CustomerPaysTheDifference()
    {
        var sale = Sell(TicketOf((_taza, 1)));       // 3,50
        var newTicket = TicketOf((_plato, 1));      // 5,00

        var result = _sales.Exchange(sale.Id, [new ReturnLineRequest(_sales.GetReturnable(sale.Id)[0].Line.Id, 1)],
            newTicket, PaymentRequest.Cash(2m), PaymentMethod.Cash, _cashierId);

        Assert.True(result.Success, result.ErrorKey);
        var (returned, newSale, difference, change) = result.Value!;
        Assert.Equal((1.50m, 0.50m), (difference, change));
        Assert.Equal([(PaymentMethod.StoreCredit, -3.50m)], _sales.GetSale(returned.Id)!.Payments.Select(p => (p.Method, p.Amount)));
        Assert.Equal([(PaymentMethod.StoreCredit, 3.50m), (PaymentMethod.Cash, 1.50m)],
            _sales.GetSale(newSale.Id)!.Payments.Select(p => (p.Method, p.Amount)));
        Assert.Equal(0, _stock.GetStock(_taza.Id));
        Assert.Equal(-1, _stock.GetStock(_plato.Id));
        Assert.Equal(InvoiceType.Rectificative, _invoices.GetCurrentForSale(returned.Id)!.Type);
        Assert.Equal(InvoiceType.Simplified, _invoices.GetCurrentForSale(newSale.Id)!.Type);
        Assert.Equal(AuditActions.Exchange, Assert.Single(Audit()).Action);
        Assert.Equal(101.50m + 3.50m, _cash.GetSummary(_cash.GetOpenSession()!.Id)!.ExpectedCash); // solo entra la diferencia
    }

    [Fact]
    public void Exchange_ShopRefundsTheDifference()
    {
        var sale = Sell(TicketOf((_plato, 1)));     // 5,00
        var newTicket = TicketOf((_taza, 1));       // 3,50

        var result = _sales.Exchange(sale.Id, [new ReturnLineRequest(_sales.GetReturnable(sale.Id)[0].Line.Id, 1)],
            newTicket, PaymentRequest.Cash(0m), PaymentMethod.Cash, _adminId);

        var (returned, newSale, difference, _) = result.Value!;
        Assert.Equal(-1.50m, difference);
        Assert.Equal([(PaymentMethod.StoreCredit, -3.50m), (PaymentMethod.Cash, -1.50m)],
            _sales.GetSale(returned.Id)!.Payments.Select(p => (p.Method, p.Amount)));
        Assert.Equal([(PaymentMethod.StoreCredit, 3.50m)], _sales.GetSale(newSale.Id)!.Payments.Select(p => (p.Method, p.Amount)));
    }

    // --- USR-03 ---

    [Fact]
    public void Audit_CannotBeModifiedOrDeleted()
    {
        var ticket = TicketOf((_taza, 1));
        ticket.SetDiscountPercent(5m);
        _sales.Checkout(ticket, PaymentRequest.Cash(10m), _adminId);
        using var db = _db.Factory.CreateDbContext();

        Assert.Throws<SqliteException>(() => db.AuditEntries.ExecuteDelete());
        Assert.Throws<SqliteException>(() => db.AuditEntries.ExecuteUpdate(s => s.SetProperty(a => a.Details, "nada")));
    }
}
