using Microsoft.Extensions.Time.Testing;
using Pos.Core.Domain;
using Pos.Data;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;

namespace Pos.Modules.Tests;

/// <summary>Sección 7: variantes (BAZ-05), cambio masivo de precios (PRE-03) y ubicación para el verificador (BAZ-06).</summary>
public sealed class Section7Tests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly CatalogService _catalog;
    private readonly PriceService _prices;
    private readonly int _adminId;

    public Section7Tests()
    {
        _catalog = new CatalogService(_db.Factory);
        _prices = new PriceService(_db.Factory, _clock);
        _adminId = new UserService(_db.Factory, _clock).CreateUser("Ana", "1111", Role.Admin).Value!.Id;
    }

    public void Dispose() => _db.Dispose();

    private Product Save(string name, decimal price, int? categoryId = null, string? barcode = null, string? location = null) =>
        _catalog.SaveProduct(null, new ProductInput(name, price, 21m, barcode, categoryId, null, location)).Value!;

    // --- BAZ-05 ---

    [Fact]
    public void Variants_HaveTheirOwnBarcodeAndCommonOrOwnPrice()
    {
        var camiseta = Save("Camiseta", 9.95m, location: "Pasillo 2");

        var rojaM = _catalog.SaveVariant(camiseta.Id, null, "Roja M", "8410000000108", price: null);
        var rojaXl = _catalog.SaveVariant(camiseta.Id, null, "Roja XL", "8410000000115", price: 11.95m);

        Assert.True(rojaM.Success, rojaM.ErrorKey);
        var scanned = _catalog.FindByBarcode("8410000000108")!;
        Assert.Equal(("Camiseta · Roja M", 9.95m, "Pasillo 2"), (scanned.Name, scanned.Price, scanned.Location));
        Assert.Equal(11.95m, rojaXl.Value!.Price);
        Assert.Equal(["Roja M", "Roja XL"], _catalog.GetVariants(camiseta.Id).Select(v => v.VariantName));
        Assert.Equal([camiseta.Id], _catalog.WithVariants([camiseta.Id]));
    }

    [Fact]
    public void ChangingTheProduct_UpdatesVariantsWithTheCommonPrice()
    {
        var hogar = _catalog.SaveCategory(null, "Ropa", 0, null, false).Value!;
        var camiseta = Save("Camiseta", 9.95m);
        _catalog.SaveVariant(camiseta.Id, null, "M", null, null);
        _catalog.SaveVariant(camiseta.Id, null, "XL", null, 11.95m);

        _catalog.SaveProduct(camiseta.Id, new ProductInput("Camiseta básica", 10.95m, 10m, null, hogar.Id, null, "Pasillo 4"));

        var variants = _catalog.GetVariants(camiseta.Id);
        Assert.Equal(["Camiseta básica · M", "Camiseta básica · XL"], variants.Select(v => v.Name));
        Assert.Equal([10.95m, 11.95m], variants.Select(v => v.Price)); // la XL mantiene su precio propio
        Assert.All(variants, v => Assert.Equal((10m, hogar.Id, "Pasillo 4"), (v.VatRate, v.CategoryId!.Value, v.Location)));
    }

    [Fact]
    public void Variants_Validation()
    {
        var camiseta = Save("Camiseta", 9.95m, barcode: "111");
        var m = _catalog.SaveVariant(camiseta.Id, null, "M", null, null).Value!;

        Assert.Equal("ErrorVariantNameRequired", _catalog.SaveVariant(camiseta.Id, null, " ", null, null).ErrorKey);
        Assert.Equal("ErrorVariantNameTaken", _catalog.SaveVariant(camiseta.Id, null, "M", null, null).ErrorKey);
        Assert.Equal("ErrorBarcodeTaken", _catalog.SaveVariant(camiseta.Id, null, "L", "111", null).ErrorKey);
        Assert.Equal("ErrorVariantOfVariant", _catalog.SaveVariant(m.Id, null, "Roja", null, null).ErrorKey);
        Assert.Equal("ErrorEditVariantFromParent", _catalog.SaveProduct(m.Id, new ProductInput("X", 1m, 21m, null, null, null)).ErrorKey);
    }

    [Fact]
    public void ProductList_HidesVariants_SearchFindsThem()
    {
        var camiseta = Save("Camiseta", 9.95m);
        _catalog.SaveVariant(camiseta.Id, null, "Azul", null, null);

        Assert.Equal(["Camiseta"], _catalog.Search(null, includeVariants: false).Select(p => p.Name));
        Assert.Equal(["Camiseta · Azul"], _catalog.Search("azul").Select(p => p.Name));
    }

    [Fact]
    public void DeactivatingTheProduct_DeactivatesItsVariants()
    {
        var camiseta = Save("Camiseta", 9.95m);
        _catalog.SaveVariant(camiseta.Id, null, "M", "222", null);

        _catalog.SetProductActive(camiseta.Id, false);

        Assert.Null(_catalog.FindByBarcode("222"));
        Assert.Empty(_catalog.WithVariants([camiseta.Id]));
    }

    [Fact]
    public void Variant_IsSoldWithItsFullName()
    {
        var camiseta = Save("Camiseta", 9.95m);
        var xl = _catalog.SaveVariant(camiseta.Id, null, "XL", null, 11.95m).Value!;
        new Pos.Modules.CashRegister.CashRegisterService(_db.Factory, _clock).Open(_adminId, 0m);
        var sales = new SalesService(_db.Factory, _clock);
        var ticket = new Ticket();
        ticket.Add(TicketItem.FromProduct(xl));

        var sale = sales.Checkout(ticket, PaymentRequest.Cash(20m), _adminId).Value!.Sale;

        var line = Assert.Single(sales.GetSale(sale.Id)!.Lines);
        Assert.Equal(("Camiseta · XL", 11.95m, xl.Id), (line.Description, line.UnitPrice, line.ProductId!.Value));
    }

    // --- PRE-03 ---

    [Fact]
    public void PriceChange_ByPercentOnACategory_WithPreview()
    {
        var hogar = _catalog.SaveCategory(null, "Hogar", 0, null, false).Value!;
        var taza = Save("Taza", 3.50m, hogar.Id);
        var plato = Save("Plato", 4.99m, hogar.Id);
        var libro = Save("Libro", 10m);

        var preview = _prices.Preview(hogar.Id, PriceChangeMode.Percent, 10m);

        Assert.Equal([("Plato", 4.99m, 5.49m), ("Taza", 3.50m, 3.85m)], preview.Value!.Select(l => (l.Name, l.OldPrice, l.NewPrice)));
        Assert.Equal(3.50m, _catalog.GetProduct(taza.Id)!.Price); // la vista previa no guarda nada

        Assert.Equal(2, _prices.Apply(hogar.Id, PriceChangeMode.Percent, 10m, _adminId).Value);

        Assert.Equal((3.85m, 5.49m, 10m), (_catalog.GetProduct(taza.Id)!.Price, _catalog.GetProduct(plato.Id)!.Price, _catalog.GetProduct(libro.Id)!.Price));
        using var db = _db.Factory.CreateDbContext();
        var audit = Assert.Single(db.AuditEntries);
        Assert.Equal((AuditActions.PriceChange, "Ana", "+10 % · Hogar · 2 productos"), (audit.Action, audit.UserName, audit.Details));
    }

    [Fact]
    public void PriceChange_ByAmount_DownOnTheWholeCatalog()
    {
        Save("Taza", 3.50m);
        var camiseta = Save("Camiseta", 9.95m);
        _catalog.SaveVariant(camiseta.Id, null, "XL", null, 11.95m);

        _prices.Apply(null, PriceChangeMode.Amount, -0.50m, _adminId);

        Assert.Equal([("Camiseta", 9.45m), ("Camiseta · XL", 11.45m), ("Taza", 3.00m)],
            _catalog.Search(null).Select(p => (p.Name, p.Price)));
    }

    [Fact]
    public void PriceChange_Validation()
    {
        Save("Llavero", 0.30m);

        Assert.Equal("ErrorPriceChangeZero", _prices.Preview(null, PriceChangeMode.Percent, 0m).ErrorKey);
        Assert.Equal("ErrorPriceWouldBeNegative", _prices.Apply(null, PriceChangeMode.Amount, -1m, _adminId).ErrorKey);
        Assert.Equal("ErrorNoProducts", _prices.Preview(999, PriceChangeMode.Percent, 5m).ErrorKey);
    }
}
