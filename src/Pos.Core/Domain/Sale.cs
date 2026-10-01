namespace Pos.Core.Domain;

public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
}

/// <summary>
/// Venta cobrada. Es un dato de facturación: no se modifica ni se borra nunca
/// (la BD lo impide con triggers). Las correcciones irán por devoluciones (VEN-06).
/// </summary>
public class Sale
{
    public int Id { get; set; }

    public int CashSessionId { get; set; }

    public int UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Total con IVA.</summary>
    public decimal Total { get; set; }

    /// <summary>Efectivo entregado por el cliente (0 si pagó todo con tarjeta).</summary>
    public decimal CashTendered { get; set; }

    /// <summary>Cambio devuelto al cliente.</summary>
    public decimal Change { get; set; }

    public List<SaleLine> Lines { get; set; } = [];

    public List<Payment> Payments { get; set; } = [];
}

public class SaleLine
{
    public int Id { get; set; }

    public int SaleId { get; set; }

    /// <summary>Null en un artículo genérico de sección (BAZ-02).</summary>
    public int? ProductId { get; set; }

    /// <summary>Nombre en el momento de la venta: si luego cambia el producto, el ticket no cambia.</summary>
    public required string Description { get; set; }

    /// <summary>Categoría o sección, para los informes por sección.</summary>
    public int? CategoryId { get; set; }

    public int Quantity { get; set; }

    /// <summary>Precio unitario con IVA en el momento de la venta.</summary>
    public decimal UnitPrice { get; set; }

    public decimal VatRate { get; set; }

    public decimal LineTotal { get; set; }
}

/// <summary>Importe cobrado con cada método. La suma de los pagos es el total de la venta (VEN-04).</summary>
public class Payment
{
    public int Id { get; set; }

    public int SaleId { get; set; }

    public PaymentMethod Method { get; set; }

    public decimal Amount { get; set; }
}
