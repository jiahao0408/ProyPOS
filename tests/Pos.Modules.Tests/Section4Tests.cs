using Microsoft.Extensions.Time.Testing;
using Pos.Core.Domain;
using Pos.Core.Printing;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.Bazaar;
using Pos.Modules.CashRegister;
using Pos.Modules.Printing;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;

namespace Pos.Modules.Tests;

/// <summary>Sección 4: cierre de caja y etiquetas.</summary>
public sealed class Section4Tests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly CatalogService _catalog;
    private readonly CashRegisterService _cash;
    private readonly SalesService _sales;
    private readonly int _userId;

    public Section4Tests()
    {
        _catalog = new CatalogService(_db.Factory);
        _cash = new CashRegisterService(_db.Factory, _clock);
        _sales = new SalesService(_db.Factory, _clock);
        _userId = new UserService(_db.Factory, _clock).CreateUser("Ana", "1111", Role.Admin).Value!.Id;
    }

    public void Dispose() => _db.Dispose();

    private Product NewProduct(string name = "Vaso", string? barcode = null) =>
        _catalog.SaveProduct(null, new ProductInput(name, 2m, 21m, barcode, null, null)).Value!;

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
        var builder = new ReportBuilder(new PrintLocalization(localizer, new RegionFormatter(localizer)));
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
        var builder = new ReportBuilder(new PrintLocalization(localizer, new RegionFormatter(localizer)));

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
