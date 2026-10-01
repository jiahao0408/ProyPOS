namespace Pos.Core.Domain;

public enum StockMovementReason
{
    Sale = 0,
    Return = 1,
    Receipt = 2,
    Adjustment = 3,
    Count = 4,
}

/// <summary>Cada cambio de stock queda registrado; <see cref="Product.Stock"/> es la suma de todos.</summary>
public class StockMovement
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    /// <summary>Unidades que entran (positivo) o salen (negativo).</summary>
    public int Quantity { get; set; }

    public StockMovementReason Reason { get; set; }

    public int? SaleId { get; set; }

    public Sale? Sale { get; set; }

    public int? UserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public string? Note { get; set; }
}
