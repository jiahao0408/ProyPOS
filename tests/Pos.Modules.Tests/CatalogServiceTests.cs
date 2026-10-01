using Pos.Modules.Products;

namespace Pos.Modules.Tests;

public sealed class CatalogServiceTests : IDisposable
{
    private readonly TestDatabase _db = new();
    private readonly CatalogService _catalog;

    public CatalogServiceTests() => _catalog = new CatalogService(_db.Factory);

    public void Dispose() => _db.Dispose();

    private static ProductInput Input(string name = "Taza", decimal price = 3.50m, decimal vat = 21m, string? barcode = null, int? categoryId = null) =>
        new(name, price, vat, barcode, categoryId, PhotoPath: null);

    [Fact]
    public void SaveProduct_CreatesProduct()
    {
        var result = _catalog.SaveProduct(null, Input(barcode: "8412345678905"));

        Assert.True(result.Success);
        Assert.Equal("Taza", _catalog.FindByBarcode("8412345678905")?.Name);
    }

    [Theory]
    [InlineData("  ", 1, 21, "ErrorNameRequired")]
    [InlineData("Taza", -1, 21, "ErrorPriceNegative")]
    [InlineData("Taza", 1.234, 21, "ErrorPriceDecimals")]
    [InlineData("Taza", 1, 7, "ErrorVatRate")]
    public void SaveProduct_ValidatesRequiredFields(string name, decimal price, decimal vat, string expectedError)
    {
        // PRE-01: campos obligatorios nombre, precio y tipo de IVA.
        Assert.Equal(expectedError, _catalog.SaveProduct(null, Input(name, price, vat)).ErrorKey);
    }

    [Fact]
    public void SaveProduct_BarcodeMustBeUnique()
    {
        _catalog.SaveProduct(null, Input("Taza", barcode: "123"));

        var duplicate = _catalog.SaveProduct(null, Input("Plato", barcode: "123"));

        Assert.Equal("ErrorBarcodeTaken", duplicate.ErrorKey);
    }

    [Fact]
    public void SaveProduct_ManyProductsWithoutBarcode()
    {
        Assert.True(_catalog.SaveProduct(null, Input("Taza")).Success);
        Assert.True(_catalog.SaveProduct(null, Input("Plato")).Success);
    }

    [Fact]
    public void SaveProduct_EditKeepsOwnBarcode()
    {
        var product = _catalog.SaveProduct(null, Input("Taza", barcode: "123")).Value!;

        var edited = _catalog.SaveProduct(product.Id, Input("Taza grande", 4m, barcode: "123"));

        Assert.True(edited.Success);
        Assert.Equal("Taza grande", _catalog.GetProduct(product.Id)!.Name);
    }

    [Fact]
    public void Search_FindsByPartialNameOrExactBarcode()
    {
        _catalog.SaveProduct(null, Input("Taza de café", barcode: "111"));
        _catalog.SaveProduct(null, Input("Plato hondo", barcode: "222"));

        Assert.Single(_catalog.Search("café"));
        Assert.Equal("Plato hondo", Assert.Single(_catalog.Search("222")).Name);
        Assert.Equal(2, _catalog.Search("").Count);
    }

    [Fact]
    public void Search_TreatsLikeWildcardsAsText()
    {
        _catalog.SaveProduct(null, Input("Descuento 50%"));
        _catalog.SaveProduct(null, Input("Taza"));

        Assert.Equal("Descuento 50%", Assert.Single(_catalog.Search("%")).Name);
    }

    [Fact]
    public void Search_FiltersByCategoryAndHidesInactive()
    {
        var cocina = _catalog.SaveCategory(null, "Cocina", 0, null, false).Value!;
        var taza = _catalog.SaveProduct(null, Input("Taza", categoryId: cocina.Id)).Value!;
        _catalog.SaveProduct(null, Input("Lápiz"));
        _catalog.SaveProduct(null, Input("Plato", categoryId: cocina.Id));

        _catalog.SetProductActive(taza.Id, false);

        Assert.Equal("Plato", Assert.Single(_catalog.Search(null, cocina.Id)).Name);
        Assert.Equal(2, _catalog.Search(null, cocina.Id, includeInactive: true).Count);
    }

    [Fact]
    public void Search_RespectsLimit()
    {
        for (var i = 0; i < 10; i++)
            _catalog.SaveProduct(null, Input($"Producto {i}"));

        Assert.Equal(5, _catalog.Search(null, limit: 5).Count);
    }

    [Fact]
    public void Categories_AreOrderedAndUnique()
    {
        _catalog.SaveCategory(null, "Papelería", 2, null, false);
        _catalog.SaveCategory(null, "Hogar", 1, "#FF8800", true);

        Assert.Equal(["Hogar", "Papelería"], _catalog.GetCategories().Select(c => c.Name));
        Assert.Equal("ErrorCategoryNameTaken", _catalog.SaveCategory(null, "Hogar", 3, null, false).ErrorKey);
        Assert.Equal("ErrorColorFormat", _catalog.SaveCategory(null, "Juguetes", 3, "rojo", false).ErrorKey);
    }

    [Fact]
    public void DeleteCategory_KeepsItsProducts()
    {
        var hogar = _catalog.SaveCategory(null, "Hogar", 0, null, false).Value!;
        var taza = _catalog.SaveProduct(null, Input("Taza", categoryId: hogar.Id)).Value!;

        _catalog.DeleteCategory(hogar.Id);

        var product = _catalog.GetProduct(taza.Id);
        Assert.NotNull(product);
        Assert.Null(product.CategoryId);
    }
}
