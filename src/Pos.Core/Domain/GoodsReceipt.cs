namespace Pos.Core.Domain;

public class Supplier
{
    public int Id { get; set; }

    public required string Name { get; set; }

    public string? Nif { get; set; }

    public string? Phone { get; set; }
}

/// <summary>Entrada de mercancía (INV-03): suma stock y actualiza el coste medio.</summary>
public class GoodsReceipt
{
    public int Id { get; set; }

    public int? SupplierId { get; set; }

    /// <summary>Número de albarán o factura del proveedor.</summary>
    public string? Reference { get; set; }

    public int UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public decimal TotalCost { get; set; }

    public List<GoodsReceiptLine> Lines { get; set; } = [];
}

public class GoodsReceiptLine
{
    public int Id { get; set; }

    public int GoodsReceiptId { get; set; }

    public int ProductId { get; set; }

    /// <summary>BAZ-04: cajas recibidas, si se recibió por cajas.</summary>
    public int? Boxes { get; set; }

    /// <summary>Unidades que entran en stock (cajas × unidades por caja).</summary>
    public int Units { get; set; }

    /// <summary>Coste de cada unidad, sin IVA.</summary>
    public decimal UnitCost { get; set; }

    public decimal LineCost { get; set; }
}
