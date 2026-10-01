using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Pos.Core;
using Pos.Data;

namespace Pos.Modules.Products;

public enum PriceChangeMode
{
    Percent,
    Amount,
}

public sealed record PriceChangeLine(int ProductId, string Name, decimal OldPrice, decimal NewPrice);

/// <summary>
/// PRE-03: subir o bajar precios de varios productos a la vez, por porcentaje o importe, sobre una
/// categoría o todo el catálogo. Un valor positivo sube y uno negativo baja. Primero se ve la vista
/// previa; al aplicarla queda en la auditoría (USR-03).
/// </summary>
public sealed class PriceService(IDbContextFactory<PosDbContext> dbFactory, TimeProvider clock)
{
    public OperationResult<IReadOnlyList<PriceChangeLine>> Preview(int? categoryId, PriceChangeMode mode, decimal value)
    {
        using var db = dbFactory.CreateDbContext();
        return Compute(db, categoryId, mode, value);
    }

    /// <summary>Aplica el cambio en una sola transacción. Devuelve cuántos precios han cambiado.</summary>
    public OperationResult<int> Apply(int? categoryId, PriceChangeMode mode, decimal value, int userId)
    {
        using var db = dbFactory.CreateDbContext();
        var computed = Compute(db, categoryId, mode, value);
        if (!computed.Success)
            return OperationResult<int>.Fail(computed.ErrorKey!);

        var changes = computed.Value!.Where(l => l.NewPrice != l.OldPrice).ToDictionary(l => l.ProductId);
        var ids = changes.Keys.ToList();
        foreach (var product in db.Products.Where(p => ids.Contains(p.Id)))
            product.Price = changes[product.Id].NewPrice;

        var category = categoryId is { } id ? db.Categories.Find(id)?.Name : null;
        var amount = mode == PriceChangeMode.Percent
            ? $"{value.ToString("+0.##;-0.##", CultureInfo.InvariantCulture)} %"
            : $"{value.ToString("+0.00;-0.00", CultureInfo.InvariantCulture)} €";
        AuditLog.Record(db, db.Users.Find(userId), AuditActions.PriceChange,
            $"{amount} · {category ?? "todo el catálogo"} · {changes.Count} productos",
            clock.GetUtcNow().UtcDateTime);
        db.SaveChanges();
        return OperationResult<int>.Ok(changes.Count);
    }

    public static decimal NewPrice(decimal price, PriceChangeMode mode, decimal value) =>
        Math.Round(mode == PriceChangeMode.Percent ? price * (1 + value / 100m) : price + value, 2, MidpointRounding.AwayFromZero);

    private static OperationResult<IReadOnlyList<PriceChangeLine>> Compute(PosDbContext db, int? categoryId, PriceChangeMode mode, decimal value)
    {
        if (value == 0)
            return OperationResult<IReadOnlyList<PriceChangeLine>>.Fail("ErrorPriceChangeZero");

        var query = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (categoryId is not null)
            query = query.Where(p => p.CategoryId == categoryId);

        var lines = query.OrderBy(p => p.Name).AsEnumerable()
            .Select(p => new PriceChangeLine(p.Id, p.Name, p.Price, NewPrice(p.Price, mode, value)))
            .ToList();
        if (lines.Count == 0)
            return OperationResult<IReadOnlyList<PriceChangeLine>>.Fail("ErrorNoProducts");
        if (lines.Any(l => l.NewPrice < 0))
            return OperationResult<IReadOnlyList<PriceChangeLine>>.Fail("ErrorPriceWouldBeNegative");
        return OperationResult<IReadOnlyList<PriceChangeLine>>.Ok(lines);
    }
}
