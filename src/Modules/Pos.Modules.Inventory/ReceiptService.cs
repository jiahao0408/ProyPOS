using Microsoft.EntityFrameworkCore;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Data;

namespace Pos.Modules.Inventory;

/// <summary>
/// Una línea de la entrada tal como la teclea el admin. BAZ-04: si <paramref name="InBoxes"/>,
/// la cantidad son cajas y el coste es el de una caja; si no, unidades y coste por unidad.
/// </summary>
public sealed record ReceiptLineInput(int ProductId, int Quantity, bool InBoxes, decimal Cost);

/// <summary>Entradas de mercancía (INV-03) y compra por cajas (BAZ-04).</summary>
public sealed class ReceiptService(IDbContextFactory<PosDbContext> dbFactory, TimeProvider clock)
{
    public OperationResult<GoodsReceipt> Receive(int? supplierId, string? reference, IReadOnlyList<ReceiptLineInput> lines, int userId)
    {
        if (lines.Count == 0)
            return OperationResult<GoodsReceipt>.Fail("ErrorReceiptEmpty");
        if (lines.Any(l => l.Quantity <= 0))
            return OperationResult<GoodsReceipt>.Fail("ErrorQuantity");
        if (lines.Any(l => l.Cost < 0))
            return OperationResult<GoodsReceipt>.Fail("ErrorCostNegative");

        using var db = dbFactory.CreateDbContext();
        if (supplierId is { } sid && !db.Suppliers.Any(s => s.Id == sid))
            return OperationResult<GoodsReceipt>.Fail("ErrorSupplierNotFound");

        var ids = lines.Select(l => l.ProductId).Distinct().ToList();
        var products = db.Products.Where(p => ids.Contains(p.Id)).ToDictionary(p => p.Id);
        if (products.Count != ids.Count)
            return OperationResult<GoodsReceipt>.Fail("ErrorProductNotFound");

        var now = clock.GetUtcNow().UtcDateTime;
        var receipt = new GoodsReceipt
        {
            SupplierId = supplierId,
            Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
            UserId = userId,
            CreatedAtUtc = now,
        };

        foreach (var input in lines)
        {
            var product = products[input.ProductId];
            var units = input.InBoxes ? input.Quantity * product.UnitsPerBox : input.Quantity;
            var unitCost = Math.Round(input.InBoxes ? input.Cost / product.UnitsPerBox : input.Cost, 4, MidpointRounding.AwayFromZero);
            var lineCost = Math.Round(input.Quantity * input.Cost, 2, MidpointRounding.AwayFromZero);

            receipt.Lines.Add(new GoodsReceiptLine
            {
                ProductId = product.Id,
                Boxes = input.InBoxes ? input.Quantity : null,
                Units = units,
                UnitCost = unitCost,
                LineCost = lineCost,
            });

            product.CostPrice = AverageCost(product.Stock, product.CostPrice, units, unitCost);
            StockLedger.Record(db, product, units, StockMovementReason.Receipt, now, userId,
                note: receipt.Reference is null ? null : $"Albarán {receipt.Reference}");
        }
        receipt.TotalCost = receipt.Lines.Sum(l => l.LineCost);

        db.GoodsReceipts.Add(receipt);
        db.SaveChanges(); // stock, coste y entrada en una sola transacción
        return OperationResult<GoodsReceipt>.Ok(receipt);
    }

    /// <summary>
    /// Coste medio ponderado. Si no había stock (o era negativo por ventas sin entrada),
    /// el coste anterior no cuenta y vale el de esta entrada.
    /// </summary>
    public static decimal AverageCost(int stock, decimal currentCost, int units, decimal unitCost) =>
        stock <= 0
            ? unitCost
            : Math.Round((stock * currentCost + units * unitCost) / (stock + units), 4, MidpointRounding.AwayFromZero);

    public IReadOnlyList<GoodsReceipt> GetRecent(int limit = 50)
    {
        using var db = dbFactory.CreateDbContext();
        return db.GoodsReceipts.AsNoTracking().Include(r => r.Lines).OrderByDescending(r => r.Id).Take(limit).ToList();
    }

    // --- Proveedores ---

    public IReadOnlyList<Supplier> GetSuppliers()
    {
        using var db = dbFactory.CreateDbContext();
        return db.Suppliers.AsNoTracking().OrderBy(s => s.Name).ToList();
    }

    public OperationResult<Supplier> CreateSupplier(string name, string? nif = null, string? phone = null)
    {
        name = name.Trim();
        if (name.Length == 0)
            return OperationResult<Supplier>.Fail("ErrorNameRequired");

        using var db = dbFactory.CreateDbContext();
        if (db.Suppliers.Any(s => s.Name == name))
            return OperationResult<Supplier>.Fail("ErrorSupplierNameTaken");

        var supplier = new Supplier
        {
            Name = name,
            Nif = string.IsNullOrWhiteSpace(nif) ? null : nif.Trim(),
            Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim(),
        };
        db.Suppliers.Add(supplier);
        db.SaveChanges();
        return OperationResult<Supplier>.Ok(supplier);
    }
}
