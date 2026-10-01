namespace Pos.Core.Domain;

public class Category
{
    public int Id { get; set; }

    public required string Name { get; set; }

    /// <summary>Orden de los botones en la pantalla de venta (PRE-02).</summary>
    public int SortOrder { get; set; }

    /// <summary>Color del botón en formato #RRGGBB.</summary>
    public string? Color { get; set; }

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

    public bool IsActive { get; set; } = true;
}
