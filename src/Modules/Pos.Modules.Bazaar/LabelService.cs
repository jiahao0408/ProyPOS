using Microsoft.EntityFrameworkCore;
using Pos.Core;
using Pos.Core.Printing;
using Pos.Data;

namespace Pos.Modules.Bazaar;

public sealed record LabelRequest(int ProductId, int Copies);

/// <summary>
/// BAZ-01: etiquetas de precio con código de barras. A los productos que llegan sin código
/// se les asigna uno interno (EAN-13 que empieza por 29) al imprimir su etiqueta.
/// </summary>
public sealed class LabelService(IDbContextFactory<PosDbContext> dbFactory)
{
    public const int MaxCopies = 500;

    public OperationResult<IReadOnlyList<LabelItem>> Prepare(IReadOnlyList<LabelRequest> requests)
    {
        var wanted = requests.Where(r => r.Copies > 0).ToList();
        if (wanted.Count == 0)
            return OperationResult<IReadOnlyList<LabelItem>>.Fail("ErrorNoLabels");
        if (wanted.Any(r => r.Copies > MaxCopies))
            return OperationResult<IReadOnlyList<LabelItem>>.Fail("ErrorTooManyLabels");

        using var db = dbFactory.CreateDbContext();
        var ids = wanted.Select(r => r.ProductId).Distinct().ToList();
        var products = db.Products.Where(p => ids.Contains(p.Id)).ToDictionary(p => p.Id);
        if (products.Count != ids.Count)
            return OperationResult<IReadOnlyList<LabelItem>>.Fail("ErrorProductNotFound");

        foreach (var product in products.Values.Where(p => string.IsNullOrEmpty(p.Barcode)))
        {
            var code = Ean13.Internal(product.Id);
            if (db.Products.Any(p => p.Barcode == code))
                return OperationResult<IReadOnlyList<LabelItem>>.Fail("ErrorBarcodeTaken");
            product.Barcode = code;
        }
        db.SaveChanges();

        IReadOnlyList<LabelItem> labels = wanted
            .Select(r => products[r.ProductId])
            .Zip(wanted, (p, r) => new LabelItem(p.Name, p.Price, p.Barcode!, r.Copies))
            .ToList();
        return OperationResult<IReadOnlyList<LabelItem>>.Ok(labels);
    }
}
