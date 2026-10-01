using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pos.Core.Domain;
using Pos.Modules.CashRegister;
using Pos.Modules.Inventory;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;

namespace Pos.Modules.Tests;

public sealed class SalesServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly SalesService _sales;
    private readonly CatalogService _catalog;
    private readonly CashRegisterService _cash;
    private readonly StockService _stock;
    private readonly int _userId;
    private readonly Product _taza;

    public SalesServiceTests()
    {
        _sales = new SalesService(_db.Factory, _clock);
        _catalog = new CatalogService(_db.Factory);
        _cash = new CashRegisterService(_db.Factory, _clock);
        _stock = new StockService(_db.Factory);
        _userId = new UserService(_db.Factory, _clock).CreateUser("Luis", "2222", Role.Cashier).Value!.Id;
        _taza = _catalog.SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "111", null, null)).Value!;
        _cash.Open(_userId, 100m);
    }

    public void Dispose() => _db.Dispose();

    private Ticket TicketWith(int tazas)
    {
        var ticket = new Ticket();
        ticket.Add(TicketItem.FromProduct(_taza), tazas);
        return ticket;
    }

    [Fact]
    public void Cash_ShowsChangeAndRecordsPayment()
    {
        // VEN-03: introduzco lo entregado y la app muestra el cambio.
        var result = _sales.Checkout(TicketWith(3), PaymentRequest.Cash(20m), _userId);

        Assert.True(result.Success);
        Assert.Equal(9.50m, result.Value!.Change);
        var sale = _sales.GetSale(result.Value.Sale.Id)!;
        Assert.Equal(10.50m, sale.Total);
        Assert.Equal(20m, sale.CashTendered);
        var payment = Assert.Single(sale.Payments);
        Assert.Equal((PaymentMethod.Cash, 10.50m), (payment.Method, payment.Amount));
    }

    [Fact]
    public void Cash_CannotChargeLessThanTotal()
    {
        Assert.Equal("ErrorCashInsufficient", _sales.Checkout(TicketWith(3), PaymentRequest.Cash(10m), _userId).ErrorKey);
    }

    [Fact]
    public void Card_PaysExactTotal()
    {
        var result = _sales.Checkout(TicketWith(2), PaymentRequest.Card(7m), _userId);

        var payment = Assert.Single(_sales.GetSale(result.Value!.Sale.Id)!.Payments);
        Assert.Equal((PaymentMethod.Card, 7m), (payment.Method, payment.Amount));
        Assert.Equal(0m, result.Value.Change);
    }

    [Fact]
    public void Mixed_PaymentsAddUpToTotal()
    {
        // VEN-04: se registra el importe de cada método; la suma debe igualar el total.
        var result = _sales.Checkout(TicketWith(3), new PaymentRequest(CardAmount: 5m, CashTendered: 10m), _userId);

        Assert.True(result.Success);
        Assert.Equal(4.50m, result.Value!.Change); // efectivo debido 5,50, entregado 10
        var sale = _sales.GetSale(result.Value.Sale.Id)!;
        Assert.Equal(sale.Total, sale.Payments.Sum(p => p.Amount));
        Assert.Equal(5.50m, sale.Payments.Single(p => p.Method == PaymentMethod.Cash).Amount);
    }

    [Fact]
    public void Card_CannotExceedTotal()
    {
        Assert.Equal("ErrorCardExceedsTotal", _sales.Checkout(TicketWith(1), PaymentRequest.Card(10m), _userId).ErrorKey);
    }

    [Fact]
    public void EmptyTicket_IsRejected()
    {
        Assert.Equal("ErrorTicketEmpty", _sales.Checkout(new Ticket(), PaymentRequest.Cash(10m), _userId).ErrorKey);
    }

    [Fact]
    public void ClosedCashRegister_IsRejected()
    {
        using var db = _db.Factory.CreateDbContext();
        db.CashSessions.ExecuteUpdate(s => s.SetProperty(x => x.ClosedAtUtc, DateTime.UtcNow));

        Assert.Equal("ErrorCashClosed", _sales.Checkout(TicketWith(1), PaymentRequest.Cash(10m), _userId).ErrorKey);
    }

    [Fact]
    public void Sale_DecrementsStock()
    {
        // INV-01: vender 3 unidades resta 3.
        _sales.Checkout(TicketWith(3), PaymentRequest.Cash(20m), _userId);

        Assert.Equal(-3, _stock.GetStock(_taza.Id));
        var movement = Assert.Single(_stock.GetMovements(_taza.Id));
        Assert.Equal((-3, StockMovementReason.Sale), (movement.Quantity, movement.Reason));
        Assert.NotNull(movement.SaleId);
    }

    [Fact]
    public void GenericItem_DoesNotTouchStock()
    {
        var hogar = _catalog.SaveCategory(null, "Hogar", 0, null, true).Value!;
        var ticket = new Ticket();
        ticket.Add(TicketItem.Generic(hogar, 4.95m));

        var result = _sales.Checkout(ticket, PaymentRequest.Cash(5m), _userId);

        var line = Assert.Single(_sales.GetSale(result.Value!.Sale.Id)!.Lines);
        Assert.Null(line.ProductId);
        Assert.Equal(hogar.Id, line.CategoryId); // BAZ-02: aparece en ventas por sección
        Assert.Equal(0, _stock.GetStock(_taza.Id));
    }

    [Fact]
    public void Sale_KeepsNameAndPriceAtTimeOfSale()
    {
        var result = _sales.Checkout(TicketWith(1), PaymentRequest.Cash(5m), _userId);
        _catalog.SaveProduct(_taza.Id, new ProductInput("Taza grande", 9m, 21m, "111", null, null));

        var line = Assert.Single(_sales.GetSale(result.Value!.Sale.Id)!.Lines);
        Assert.Equal(("Taza", 3.50m), (line.Description, line.UnitPrice));
    }

    [Fact]
    public void Sales_CannotBeModifiedOrDeleted()
    {
        // Los datos de facturación no se pueden borrar.
        var id = _sales.Checkout(TicketWith(1), PaymentRequest.Cash(5m), _userId).Value!.Sale.Id;
        using var db = _db.Factory.CreateDbContext();

        Assert.Throws<SqliteException>(() => db.Sales.Where(s => s.Id == id).ExecuteDelete());
        Assert.Throws<SqliteException>(() => db.Sales.Where(s => s.Id == id).ExecuteUpdate(s => s.SetProperty(x => x.Total, 0m)));
        Assert.Throws<SqliteException>(() => db.SaleLines.ExecuteDelete());
        Assert.Throws<SqliteException>(() => db.Payments.ExecuteUpdate(p => p.SetProperty(x => x.Amount, 0m)));
        Assert.Equal(3.50m, _sales.GetSale(id)!.Total);
    }

    [Fact]
    public void QuickCreate_ByCashierIsPendingReview()
    {
        // BAZ-03: el cajero lo crea pendiente de revisión por el admin.
        var byCashier = _catalog.QuickCreate("Pelota", 2m, null, "999", createdByAdmin: false);
        var byAdmin = _catalog.QuickCreate("Cometa", 6m, null, "998", createdByAdmin: true);

        Assert.True(byCashier.Value!.PendingReview);
        Assert.False(byAdmin.Value!.PendingReview);
        Assert.Equal("Pelota", Assert.Single(_catalog.GetPendingReview()).Name);
        Assert.Equal("ErrorBarcodeTaken", _catalog.QuickCreate("Otra", 1m, null, "999", false).ErrorKey);
    }

    [Fact]
    public void Preview_ComputesCashDueAndChange()
    {
        Assert.Equal((5.50m, 4.50m), SalesService.Preview(10.50m, new PaymentRequest(5m, 10m)));
        Assert.Equal((10.50m, 0m), SalesService.Preview(10.50m, PaymentRequest.Cash(0m)));
    }
}
