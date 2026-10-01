using Microsoft.Extensions.Time.Testing;
using Pos.Core.Domain;
using Pos.Core.Printing;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.Bazaar;
using Pos.Modules.CashRegister;
using Pos.Modules.Inventory;
using Pos.Modules.Printing;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;

namespace Pos.Modules.Tests;

/// <summary>Sección 4: entradas de mercancía, cierre de caja y etiquetas.</summary>
public sealed class Section4Tests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly CatalogService _catalog;
    private readonly ReceiptService _receipts;
    private readonly StockService _stock;
    private readonly CashRegisterService _cash;
    private readonly SalesService _sales;
    private readonly int _userId;

    public Section4Tests()
    {
        _catalog = new CatalogService(_db.Factory);
        _receipts = new ReceiptService(_db.Factory, _clock);
        _stock = new StockService(_db.Factory);
        _cash = new CashRegisterService(_db.Factory, _clock);
        _sales = new SalesService(_db.Factory, _clock);
        _userId = new UserService(_db.Factory, _clock).CreateUser("Ana", "1111", Role.Admin).Value!.Id;
    }

    public void Dispose() => _db.Dispose();

    private Product NewProduct(string name = "Vaso", int unitsPerBox = 12, string? barcode = null) =>
        _catalog.SaveProduct(null, new ProductInput(name, 2m, 21m, barcode, null, null, unitsPerBox)).Value!;

    // --- INV-03 / BAZ-04 ---

    [Fact]
    public void Receive_ByBoxes_AddsUnitsAndComputesUnitCost()
    {
        // BAZ-04: recibir 3 cajas de 12 suma 36 unidades; coste unitario calculado.
        var vaso = NewProduct();

        var result = _receipts.Receive(null, "ALB-001", [new ReceiptLineInput(vaso.Id, 3, InBoxes: true, Cost: 18m)], _userId);

        Assert.True(result.Success, result.ErrorKey);
        Assert.Equal(36, _stock.GetStock(vaso.Id));
        var line = Assert.Single(result.Value!.Lines);
        Assert.Equal((3, 36, 1.5m, 54m), (line.Boxes, line.Units, line.UnitCost, line.LineCost));
        Assert.Equal(1.5m, _catalog.GetProduct(vaso.Id)!.CostPrice);
    }

    [Fact]
    public void Receive_UpdatesWeightedAverageCost()
    {
        // INV-03: actualiza stock y coste medio.
        var vaso = NewProduct();
        _receipts.Receive(null, null, [new ReceiptLineInput(vaso.Id, 10, false, 1.00m)], _userId);

        _receipts.Receive(null, null, [new ReceiptLineInput(vaso.Id, 30, false, 2.00m)], _userId);

        Assert.Equal(40, _stock.GetStock(vaso.Id));
        Assert.Equal(1.75m, _catalog.GetProduct(vaso.Id)!.CostPrice); // (10×1 + 30×2) / 40
    }

    [Fact]
    public void Receive_AfterNegativeStock_UsesNewCost()
    {
        Assert.Equal(3m, ReceiptService.AverageCost(stock: -5, currentCost: 1m, units: 10, unitCost: 3m));
    }

    [Fact]
    public void Receive_RecordsSupplierAndMovement()
    {
        var supplier = _receipts.CreateSupplier("Mayorista Oriente").Value!;
        var vaso = NewProduct();

        _receipts.Receive(supplier.Id, "ALB-7", [new ReceiptLineInput(vaso.Id, 2, true, 10m)], _userId);

        var movement = Assert.Single(_stock.GetMovements(vaso.Id));
        Assert.Equal((24, StockMovementReason.Receipt), (movement.Quantity, movement.Reason));
        Assert.Equal(supplier.Id, Assert.Single(_receipts.GetRecent()).SupplierId);
        Assert.Equal("ErrorSupplierNameTaken", _receipts.CreateSupplier("Mayorista Oriente").ErrorKey);
    }

    [Theory]
    [InlineData(0, 1, "ErrorQuantity")]
    [InlineData(1, -1, "ErrorCostNegative")]
    public void Receive_Validates(int quantity, decimal cost, string expectedError)
    {
        var vaso = NewProduct();

        Assert.Equal(expectedError, _receipts.Receive(null, null, [new ReceiptLineInput(vaso.Id, quantity, false, cost)], _userId).ErrorKey);
        Assert.Equal("ErrorReceiptEmpty", _receipts.Receive(null, null, [], _userId).ErrorKey);
    }

    // --- CAJ-02 ---

    private void Sell(decimal price, PaymentRequest payment)
    {
        var ticket = new Ticket();
        ticket.Add(new TicketItem(null, "Genérico", null, price, 21m));
        Assert.True(_sales.Checkout(ticket, payment, _userId).Success);
    }

    [Fact]
    public void Close_ComputesExpectedCashAndDifference()
    {
        // CAJ-02: introduzco el efectivo contado; muestra el descuadre.
        _cash.Open(_userId, 100m);
        Sell(10m, PaymentRequest.Cash(20m));         // +10 en efectivo
        Sell(20m, PaymentRequest.Card(20m));         // tarjeta
        Sell(30m, new PaymentRequest(10m, 20m));     // +20 en efectivo, 10 con tarjeta

        var result = _cash.Close(_userId, countedCash: 128m);

        Assert.True(result.Success);
        var summary = result.Value!;
        Assert.Equal((3, 60m, 30m, 30m, 130m), (summary.SalesCount, summary.SalesTotal, summary.CashTotal, summary.CardTotal, summary.ExpectedCash));
        Assert.Equal(-2m, summary.Session.Difference); // faltan 2 €
        Assert.Equal(1, summary.Session.ZNumber);
        Assert.Equal(60m, Assert.Single(summary.VatLines).Total);
        Assert.False(_cash.IsOpen);
    }

    [Fact]
    public void Close_ThenSellingIsBlocked_AndZNumbersAreCorrelative()
    {
        _cash.Open(_userId, 50m);
        _cash.Close(_userId, 50m);
        Assert.Equal("ErrorCashClosed", _cash.Close(_userId, 0m).ErrorKey);

        var ticket = new Ticket();
        ticket.Add(new TicketItem(null, "X", null, 1m, 21m));
        Assert.Equal("ErrorCashClosed", _sales.Checkout(ticket, PaymentRequest.Cash(1m), _userId).ErrorKey);

        _cash.Open(_userId, 50m);
        Assert.Equal(2, _cash.Close(_userId, 50m).Value!.Session.ZNumber);
    }

    [Fact]
    public void ZReport_ShowsTotalsAndDifference()
    {
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "locales"));
        var builder = new ReportBuilder(localizer, new RegionFormatter(localizer));
        var z = new ZReportDocument(7, DateTime.UtcNow, DateTime.UtcNow, "Ana", 3, 60m, 30m, 30m, 100m, 130m, 128m,
            [new ZReportVatLine(21m, 49.59m, 10.41m, 60m)]);

        var text = TextPreview.Render(builder.BuildZReport(z, InvoicingTests.Business, PrinterProfile.Default), PrinterProfile.Default);

        Assert.Contains("CIERRE Z Nº 7", text);
        Assert.Contains("130,00", text);
        Assert.Contains("-2,00", text);
    }

    // --- BAZ-01 ---

    [Fact]
    public void Labels_AssignInternalEanToProductsWithoutBarcode()
    {
        // BAZ-01: productos que llegan sin código.
        var labels = new LabelService(_db.Factory);
        var sinCodigo = NewProduct("Llavero");
        var conCodigo = NewProduct("Taza", barcode: "8410000000011");

        var result = labels.Prepare([new LabelRequest(sinCodigo.Id, 3), new LabelRequest(conCodigo.Id, 1)]);

        Assert.True(result.Success);
        var internalCode = result.Value![0].Barcode;
        Assert.StartsWith("29", internalCode);
        Assert.True(Ean13.IsValid(internalCode));
        Assert.Equal(internalCode, _catalog.GetProduct(sinCodigo.Id)!.Barcode); // se guarda: ya se puede escanear
        Assert.Equal(("8410000000011", 1), (result.Value[1].Barcode, result.Value[1].Copies));
    }

    [Fact]
    public void Labels_PrintNamePriceAndBarcode()
    {
        var localizer = new JsonLocalizer(Path.Combine(AppContext.BaseDirectory, "locales"));
        var builder = new ReportBuilder(localizer, new RegionFormatter(localizer));

        var elements = builder.BuildLabels([new LabelItem("Llavero", 1.95m, "2900000000018", 2)], PrinterProfile.Default);

        Assert.Equal(2, elements.OfType<ReceiptBarcode>().Count());
        Assert.Equal(2, elements.OfType<ReceiptCut>().Count());
        var bytes = EscPosEncoder.Encode(elements, PrinterProfile.Default);
        Assert.Contains((byte)67, bytes); // GS k 67 = EAN-13
    }

    [Theory]
    [InlineData("8410000000016", true)]
    [InlineData("8410000000012", false)]
    [InlineData("841000000001", false)]
    public void Ean13_Validation(string code, bool valid)
    {
        Assert.Equal(valid, Ean13.IsValid(code));
    }
}
