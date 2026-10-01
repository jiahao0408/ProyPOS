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
    string? Location = null);

public static class TicketDefaults
{
    /// <summary>IVA de los productos creados con el alta rápida: el tipo general. El admin lo corrige al revisar.</summary>
    public const decimal VatRate = 21m;
}

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
    /// <param name="includeVariants">BAZ-05: false para listar solo los productos (las variantes se ven dentro de su producto).</param>
    public IReadOnlyList<Product> Search(string? text, int? categoryId = null, bool includeInactive = false, int limit = DefaultSearchLimit,
        bool includeVariants = true)
    {
        using var db = dbFactory.CreateDbContext();
        var query = db.Products.AsNoTracking().Include(p => p.Category).AsQueryable();

        if (!includeVariants)
            query = query.Where(p => p.ParentProductId == null);

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

    /// <summary>
    /// BAZ-03: alta rápida al escanear un código que no existe, sin parar la venta.
    /// Si la hace un cajero, el producto queda pendiente de revisión por el admin.
    /// </summary>
    public OperationResult<Product> QuickCreate(string name, decimal price, int? sectionId, string barcode, bool createdByAdmin)
    {
        if (string.IsNullOrWhiteSpace(barcode))
            return OperationResult<Product>.Fail("ErrorBarcodeRequired");

        var input = new ProductInput(name, price, TicketDefaults.VatRate, barcode, sectionId, PhotoPath: null);
        return SaveProduct(null, input, pendingReview: !createdByAdmin);
    }

    public IReadOnlyList<Product> GetPendingReview()
    {
        using var db = dbFactory.CreateDbContext();
        return db.Products.AsNoTracking().Where(p => p.PendingReview && p.IsActive).OrderBy(p => p.Name).ToList();
    }

    public OperationResult<Product> SaveProduct(int? id, ProductInput input) => SaveProduct(id, input, pendingReview: false);

    private OperationResult<Product> SaveProduct(int? id, ProductInput input, bool pendingReview)
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
            product = db.Products.Include(p => p.Variants).FirstOrDefault(p => p.Id == id.Value);
            if (product is null)
                return OperationResult<Product>.Fail("ErrorProductNotFound");
            if (product.ParentProductId is not null)
                return OperationResult<Product>.Fail("ErrorEditVariantFromParent");
            PropagateToVariants(product, name, input);
        }

        product.Name = name;
        product.Price = input.Price;
        product.VatRate = input.VatRate;
        product.Barcode = barcode;
        product.CategoryId = input.CategoryId;
        product.PhotoPath = string.IsNullOrWhiteSpace(input.PhotoPath) ? null : input.PhotoPath;
        product.Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim();
        // Guardar desde la ficha de admin cuenta como revisado (BAZ-03).
        product.PendingReview = pendingReview;
        db.SaveChanges();
        return OperationResult<Product>.Ok(product);
    }

    /// <summary>
    /// Los productos no se borran (las ventas y facturas los referenciarán): se desactivan
    /// y dejan de salir en la venta. Desactivar un producto desactiva también sus variantes.
    /// </summary>
    public void SetProductActive(int id, bool isActive)
    {
        using var db = dbFactory.CreateDbContext();
        var product = db.Products.Include(p => p.Variants).FirstOrDefault(p => p.Id == id);
        if (product is null)
            return;
        product.IsActive = isActive;
        if (!isActive)
            foreach (var variant in product.Variants)
                variant.IsActive = false;
        db.SaveChanges();
    }

    // --- Variantes (BAZ-05) ---

    public static string VariantFullName(string parentName, string variantName) => $"{parentName} · {variantName}";

    public IReadOnlyList<Product> GetVariants(int parentId, bool includeInactive = false)
    {
        using var db = dbFactory.CreateDbContext();
        return db.Products.AsNoTracking()
            .Where(p => p.ParentProductId == parentId && (includeInactive || p.IsActive))
            .OrderBy(p => p.Id)
            .ToList();
    }

    /// <summary>De los productos indicados, los que tienen variantes activas (en la venta se elige la variante).</summary>
    public IReadOnlySet<int> WithVariants(IEnumerable<int> productIds)
    {
        var ids = productIds.ToList();
        using var db = dbFactory.CreateDbContext();
        return db.Products.AsNoTracking()
            .Where(p => p.ParentProductId != null && p.IsActive && ids.Contains(p.ParentProductId.Value))
            .Select(p => p.ParentProductId!.Value)
            .Distinct()
            .ToHashSet();
    }

    /// <summary>
    /// Crea o modifica una variante. Comparte el IVA, la categoría, la foto y la ubicación del producto;
    /// tiene su propio código y, si se indica, su propio precio (si no, el del producto).
    /// </summary>
    public OperationResult<Product> SaveVariant(int parentId, int? variantId, string variantName, string? barcode, decimal? price)
    {
        variantName = variantName.Trim();
        barcode = string.IsNullOrWhiteSpace(barcode) ? null : barcode.Trim();
        if (variantName.Length == 0)
            return OperationResult<Product>.Fail("ErrorVariantNameRequired");
        if (price < 0)
            return OperationResult<Product>.Fail("ErrorPriceNegative");
        if (price is { } p && decimal.Round(p, 2) != p)
            return OperationResult<Product>.Fail("ErrorPriceDecimals");

        using var db = dbFactory.CreateDbContext();
        var parent = db.Products.Find(parentId);
        if (parent is null)
            return OperationResult<Product>.Fail("ErrorProductNotFound");
        if (parent.ParentProductId is not null)
            return OperationResult<Product>.Fail("ErrorVariantOfVariant");
        if (barcode is not null && db.Products.Any(x => x.Barcode == barcode && x.Id != variantId))
            return OperationResult<Product>.Fail("ErrorBarcodeTaken");
        if (db.Products.Any(x => x.ParentProductId == parentId && x.VariantName == variantName && x.Id != variantId))
            return OperationResult<Product>.Fail("ErrorVariantNameTaken");

        Product? variant;
        if (variantId is null)
        {
            variant = new Product { Name = "", ParentProductId = parentId };
            db.Products.Add(variant);
        }
        else
        {
            variant = db.Products.FirstOrDefault(x => x.Id == variantId && x.ParentProductId == parentId);
            if (variant is null)
                return OperationResult<Product>.Fail("ErrorProductNotFound");
        }

        variant.VariantName = variantName;
        variant.Name = VariantFullName(parent.Name, variantName);
        variant.Barcode = barcode;
        variant.Price = price ?? parent.Price;
        variant.VatRate = parent.VatRate;
        variant.CategoryId = parent.CategoryId;
        variant.PhotoPath = parent.PhotoPath;
        variant.Location = parent.Location;
        variant.IsActive = true;
        variant.PendingReview = false;
        db.SaveChanges();
        return OperationResult<Product>.Ok(variant);
    }

    /// <summary>
    /// Al guardar un producto con variantes, ellas heredan nombre, IVA, categoría, foto y ubicación.
    /// Las que tenían el mismo precio que el producto siguen con el precio común; las de precio propio lo mantienen.
    /// </summary>
    private static void PropagateToVariants(Product parent, string name, ProductInput input)
    {
        foreach (var variant in parent.Variants)
        {
            if (variant.Price == parent.Price)
                variant.Price = input.Price;
            variant.Name = VariantFullName(name, variant.VariantName ?? "");
            variant.VatRate = input.VatRate;
            variant.CategoryId = input.CategoryId;
            variant.PhotoPath = string.IsNullOrWhiteSpace(input.PhotoPath) ? null : input.PhotoPath;
            variant.Location = string.IsNullOrWhiteSpace(input.Location) ? null : input.Location.Trim();
        }
    }

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static bool IsHexColor(string color) =>
        color.Length == 7 && color[0] == '#' && color.Skip(1).All(Uri.IsHexDigit);
}
