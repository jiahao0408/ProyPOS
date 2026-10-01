namespace Pos.Core.Domain;

public class Category
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Orden de los botones en la pantalla de venta (PRE-02).</summary>
    public int SortOrder { get; set; }

    /// <summary>Color del botón en formato #RRGGBB.</summary>
    public string? Color { get; set; }

    /// <summary>
    /// La categoría funciona como "sección" de bazar: sale como botón de artículo genérico
    /// con precio libre (BAZ-02). Ver "Decisiones abiertas" en el README.
    /// </summary>
    public bool AllowsGenericSale { get; set; }

    public List<Product> Products { get; set; } = [];
}

public class Product
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Precio de venta con IVA incluido, como se muestra al cliente.</summary>
    public decimal Price { get; set; }

    /// <summary>Tipo de IVA en porcentaje: 21, 10, 4 o 0.</summary>
    public decimal VatRate { get; set; }

    /// <summary>Código de barras; único si existe (PRE-01).</summary>
    public string? Barcode { get; set; }

    public string? PhotoPath { get; set; }

    public int? CategoryId { get; set; }

    public Category? Category { get; set; }

    // Stock, coste y unidades por caja: columnas de la versión con inventario. La tienda no lleva stock
    // y la aplicación ya no las usa; se dejan en la base de datos para no perder datos de instalaciones antiguas.
    public int Stock { get; set; }

    public decimal CostPrice { get; set; }

    public int UnitsPerBox { get; set; } = 1;

    /// <summary>Dónde está en la tienda ("Pasillo 3, estante B"), para el verificador de precios (BAZ-06).</summary>
    public string? Location { get; set; }

    /// <summary>
    /// BAZ-05: si es una variante (talla, color, modelo), el producto padre. Cada variante es un producto
    /// con su código y su precio; el nombre guardado ya incluye la variante ("Camiseta · Rojo M") para
    /// que tickets y facturas no dependan del padre.
    /// </summary>
    public int? ParentProductId { get; set; }

    public Product? ParentProduct { get; set; }

    /// <summary>Solo la parte de la variante: "Rojo M".</summary>
    public string? VariantName { get; set; }

    public List<Product> Variants { get; set; } = [];

    /// <summary>Creado por un cajero con el alta rápida al escanear; el admin debe revisarlo (BAZ-03).</summary>
    public bool PendingReview { get; set; }

    public bool IsActive { get; set; } = true;
}
