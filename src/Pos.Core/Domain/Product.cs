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

    /// <summary>
    /// Unidades disponibles (INV-02). Solo cambia con movimientos de stock (INV-01).
    /// Puede quedar en negativo: en un bazar no se para una venta por un stock mal contado.
    /// </summary>
    public int Stock { get; set; }

    /// <summary>Unidades por caja del mayorista: recibir 3 cajas de 12 suma 36 unidades (BAZ-04).</summary>
    public int UnitsPerBox { get; set; } = 1;

    /// <summary>Creado por un cajero con el alta rápida al escanear; el admin debe revisarlo (BAZ-03).</summary>
    public bool PendingReview { get; set; }

    public bool IsActive { get; set; } = true;
}
