using Pos.Core.Domain;

namespace Pos.Data;

/// <summary>
/// Único punto que cambia el stock: añade el movimiento y actualiza el producto en el mismo
/// contexto, para que ambos se guarden en la misma transacción que la operación que los causa.
/// </summary>
public static class StockLedger
{
    public static void Record(
        PosDbContext db,
        Product product,
        int quantity,
        StockMovementReason reason,
        DateTime nowUtc,
        int? userId,
        Sale? sale = null,
        string? note = null)
    {
        product.Stock += quantity;
        db.StockMovements.Add(new StockMovement
        {
            ProductId = product.Id,
            Quantity = quantity,
            Reason = reason,
            Sale = sale,
            UserId = userId,
            CreatedAtUtc = nowUtc,
            Note = note,
        });
    }
}
