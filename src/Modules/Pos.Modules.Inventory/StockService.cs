using Microsoft.EntityFrameworkCore;
using Pos.Core.Domain;
using Pos.Data;

namespace Pos.Modules.Inventory;

/// <summary>Consulta de stock (INV-02). Solo lectura: las entradas y ajustes son de admin (INV-03, INV-05).</summary>
public sealed class StockService(IDbContextFactory<PosDbContext> dbFactory)
{
    public int? GetStock(int productId)
    {
        using var db = dbFactory.CreateDbContext();
        return db.Products.AsNoTracking().Where(p => p.Id == productId).Select(p => (int?)p.Stock).FirstOrDefault();
    }

    public IReadOnlyList<StockMovement> GetMovements(int productId, int limit = 100)
    {
        using var db = dbFactory.CreateDbContext();
        return db.StockMovements.AsNoTracking()
            .Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.Id)
            .Take(limit)
            .ToList();
    }
}
