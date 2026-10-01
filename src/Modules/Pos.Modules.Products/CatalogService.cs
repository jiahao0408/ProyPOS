using Microsoft.EntityFrameworkCore;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Pricing;
using Pos.Data;

namespace Pos.Modules.Products;

/// <summary>Datos de un producto tal como los edita el administrador (PRE-01).</summary>
public sealed record ProductInput(
    string Name,
    decimal Price,
    decimal VatRate,
    string? Barcode,
    int? CategoryId,
    string? PhotoPath,
    int UnitsPerBox = 1);

/// <summary>Catálogo: categorías y productos (PRE-01, PRE-02).</summary>
public sealed class CatalogService(IDbContextFactory<PosDbContext> dbFactory)
{
    public const int DefaultSearchLimit = 200;

    // --- Categorías ---

    public IReadOnlyList<Category> GetCategories()
    {
        using var db = dbFactory.CreateDbContext();
        return db.Categories.AsNoTracking().OrderBy(c => c.SortOrder).ThenBy(c => c.Name).ToList();
    }

    public OperationResult<Category> SaveCategory(int? id, string name, int sortOrder, string? color, bool allowsGenericSale)
    {
        name = name.Trim();
        if (name.Length == 0)
            return OperationResult<Category>.Fail("ErrorNameRequired");
        color = string.IsNullOrWhiteSpace(color) ? null : color.Trim();
        if (color is not null && !IsHexColor(color))
            return OperationResult<Category>.Fail("ErrorColorFormat");

        using var db = dbFactory.CreateDbContext();
        if (db.Categories.Any(c => c.Name == name && c.Id != id))
            return OperationResult<Category>.Fail("ErrorCategoryNameTaken");

        Category? category;
        if (id is null)
        {
            category = new Category { Name = name };
            db.Categories.Add(category);
        }
        else
        {
            category = db.Categories.Find(id.Value);
            if (category is null)
                return OperationResult<Category>.Fail("ErrorCategoryNotFound");
        }

        category.Name = name;
        category.SortOrder = sortOrder;
        category.Color = color;
        category.AllowsGenericSale = allowsGenericSale;
        db.SaveChanges();
        return OperationResult<Category>.Ok(category);
    }

    /// <summary>Borra la categoría; sus productos quedan sin categoría, no se borran.</summary>
    public void DeleteCategory(int id)
    {
        using var db = dbFactory.CreateDbContext();
        var category = db.Categories.Include(c => c.Products).FirstOrDefault(c => c.Id == id);
        if (category is null)
            return;
        foreach (var product in category.Products)
            product.CategoryId = null;
        db.Categories.Remove(category);
        db.SaveChanges();
    }

    // --- Productos ---

    public Product? GetProduct(int id)
    {
        using var db = dbFactory.CreateDbContext();
        return db.Products.AsNoTracking().Include(p => p.Category).FirstOrDefault(p => p.Id == id);
    }

    public Product? FindByBarcode(string barcode)
    {
        barcode = barcode.Trim();
        using var db = dbFactory.CreateDbContext();
        return db.Products.AsNoTracking().FirstOrDefault(p => p.Barcode == barcode && p.IsActive);
    }

    /// <summary>
    /// Busca por código exacto o por parte del nombre, con un límite de resultados para que
    /// la lista sea rápida con 30.000 productos.
    /// </summary>
    public IReadOnlyList<Product> Search(string? text, int? categoryId = null, bool includeInactive = false, int limit = DefaultSearchLimit)
    {
        using var db = dbFactory.CreateDbContext();
        var query = db.Products.AsNoTracking().Include(p => p.Category).AsQueryable();

        if (!includeInactive)
            query = query.Where(p => p.IsActive);
        if (categoryId is not null)
            query = query.Where(p => p.CategoryId == categoryId);

        text = text?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var pattern = "%" + EscapeLike(text) + "%";
            query = query.Where(p => p.Barcode == text || EF.Functions.Like(p.Name, pattern, "\\"));
        }

        return query.OrderBy(p => p.Name).Take(limit).ToList();
    }

    public OperationResult<Product> SaveProduct(int? id, ProductInput input)
    {
        var name = input.Name.Trim();
        var barcode = string.IsNullOrWhiteSpace(input.Barcode) ? null : input.Barcode.Trim();

        if (name.Length == 0)
            return OperationResult<Product>.Fail("ErrorNameRequired");
        if (input.Price < 0)
            return OperationResult<Product>.Fail("ErrorPriceNegative");
        if (decimal.Round(input.Price, 2) != input.Price)
            return OperationResult<Product>.Fail("ErrorPriceDecimals");
        if (!VatRates.IsValid(input.VatRate))
            return OperationResult<Product>.Fail("ErrorVatRate");
        if (input.UnitsPerBox < 1)
            return OperationResult<Product>.Fail("ErrorUnitsPerBox");

        using var db = dbFactory.CreateDbContext();
        if (barcode is not null && db.Products.Any(p => p.Barcode == barcode && p.Id != id))
            return OperationResult<Product>.Fail("ErrorBarcodeTaken");
        if (input.CategoryId is { } categoryId && !db.Categories.Any(c => c.Id == categoryId))
            return OperationResult<Product>.Fail("ErrorCategoryNotFound");

        Product? product;
        if (id is null)
        {
            product = new Product { Name = name };
            db.Products.Add(product);
        }
        else
        {
            product = db.Products.Find(id.Value);
            if (product is null)
                return OperationResult<Product>.Fail("ErrorProductNotFound");
        }

        product.Name = name;
        product.Price = input.Price;
        product.VatRate = input.VatRate;
        product.Barcode = barcode;
        product.CategoryId = input.CategoryId;
        product.PhotoPath = string.IsNullOrWhiteSpace(input.PhotoPath) ? null : input.PhotoPath;
        product.UnitsPerBox = input.UnitsPerBox;
        product.PendingReview = false; // guardar desde la ficha de admin cuenta como revisado (BAZ-03)
        db.SaveChanges();
        return OperationResult<Product>.Ok(product);
    }

    /// <summary>
    /// Los productos no se borran (las ventas y facturas los referenciarán): se desactivan
    /// y dejan de salir en la venta.
    /// </summary>
    public void SetProductActive(int id, bool isActive)
    {
        using var db = dbFactory.CreateDbContext();
        var product = db.Products.Find(id);
        if (product is null)
            return;
        product.IsActive = isActive;
        db.SaveChanges();
    }

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static bool IsHexColor(string color) =>
        color.Length == 7 && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
}
