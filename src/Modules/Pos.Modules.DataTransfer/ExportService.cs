using Microsoft.EntityFrameworkCore;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Data;

namespace Pos.Modules.DataTransfer;

public enum ExportKind
{
    Products,
    Customers,
    Sales,

    /// <summary>FAC-05: facturas emitidas con base y cuota por tipo de IVA, para la gestoría.</summary>
    Invoices,
}

/// <summary>Una factura del libro de facturas emitidas (FAC-05).</summary>
/// <param name="ReplacedBy">FAC-06: factura completa que sustituye a este ticket. El ticket sale en el libro,
/// pero no suma en los totales: lo vendido ya cuenta en la factura completa.</param>
public sealed record InvoiceBookRow(
    DateTime IssuedAtLocal,
    string Code,
    InvoiceType Type,
    string? CustomerNif,
    string? CustomerName,
    string? RectifiesCode,
    IReadOnlyDictionary<decimal, (decimal Base, decimal Vat)> ByRate,
    decimal Total,
    string? ReplacedBy = null)
{
    public bool CountsInTotals => ReplacedBy is null;
}

/// <summary>Facturas de un periodo con los totales por tipo de IVA.</summary>
public sealed record InvoiceBook(
    DateOnly From,
    DateOnly To,
    string IssuerName,
    string IssuerNif,
    IReadOnlyList<decimal> Rates,
    IReadOnlyList<InvoiceBookRow> Rows)
{
    public (decimal Base, decimal Vat) TotalFor(decimal rate) =>
        (Counted.Sum(r => r.ByRate.GetValueOrDefault(rate).Base), Counted.Sum(r => r.ByRate.GetValueOrDefault(rate).Vat));

    public decimal Total => Counted.Sum(r => r.Total);

    private IEnumerable<InvoiceBookRow> Counted => Rows.Where(r => r.CountsInTotals);
}

/// <summary>
/// DAT-02: exportar productos, clientes y ventas a CSV o Excel, con los encabezados en el idioma de la
/// interfaz y filtro por fechas (ventas). FAC-05: facturas emitidas por periodo con base y cuota por tipo de IVA.
/// La tienda no lleva stock, así que no se exporta.
/// </summary>
public sealed class ExportService(IDbContextFactory<PosDbContext> dbFactory, ILocalizer localizer)
{
    public static bool NeedsDates(ExportKind kind) => kind is ExportKind.Sales or ExportKind.Invoices;

    /// <summary>Escribe el fichero (.csv o .xlsx según la extensión). Devuelve cuántas filas de datos tiene.</summary>
    public int Export(ExportKind kind, string path, DateOnly from, DateOnly to)
    {
        var (headers, rows) = kind switch
        {
            ExportKind.Products => Products(),
            ExportKind.Customers => Customers(),
            ExportKind.Sales => Sales(from, to),
            _ => Invoices(LoadInvoiceBook(from, to)),
        };
        TabularFile.Write(path, headers, rows, localizer.Culture);
        return kind == ExportKind.Invoices ? rows.Count - 1 : rows.Count; // las facturas llevan una fila de totales
    }

    /// <summary>Facturas emitidas entre dos fechas (ambas incluidas, en hora local), en orden de emisión.</summary>
    public InvoiceBook LoadInvoiceBook(DateOnly from, DateOnly to)
    {
        var (fromUtc, toUtc) = UtcRange(ref from, ref to);
        using var db = dbFactory.CreateDbContext();
        var invoices = db.Invoices.AsNoTracking()
            .Where(i => i.IssuedAtUtc >= fromUtc && i.IssuedAtUtc < toUtc)
            .Include(i => i.VatLines)
            .OrderBy(i => i.IssuedAtUtc).ThenBy(i => i.Id)
            .ToList();
        var rectifiedIds = invoices.Select(i => i.RectifiedInvoiceId).OfType<int>().Distinct().ToList();
        var codes = db.Invoices.AsNoTracking().Where(i => rectifiedIds.Contains(i.Id)).ToDictionary(i => i.Id, i => i.Code);
        var ids = invoices.Select(i => i.Id).ToList();
        var replacedBy = db.Invoices.AsNoTracking()
            .Where(i => i.ReplacesInvoiceId != null && ids.Contains(i.ReplacesInvoiceId.Value))
            .ToDictionary(i => i.ReplacesInvoiceId!.Value, i => i.Code);

        var rows = invoices.Select(i => new InvoiceBookRow(
                i.IssuedAtUtc.ToLocalTime(), i.Code, i.Type, i.CustomerNif, i.CustomerName,
                i.RectifiedInvoiceId is { } r ? codes.GetValueOrDefault(r) : null,
                i.VatLines.GroupBy(v => v.Rate).ToDictionary(g => g.Key, g => (g.Sum(v => v.Base), g.Sum(v => v.VatAmount))),
                i.Total,
                replacedBy.GetValueOrDefault(i.Id)))
            .ToList();
        var rates = invoices.SelectMany(i => i.VatLines).Select(v => v.Rate).Distinct().OrderByDescending(r => r).ToList();
        var issuer = invoices.LastOrDefault();
        return new InvoiceBook(from, to, issuer?.IssuerName ?? "", issuer?.IssuerNif ?? "", rates, rows);
    }

    public string TypeName(InvoiceType type) => localizer[type switch
    {
        InvoiceType.Complete => "InvoiceCompleteShort",
        InvoiceType.Rectificative => "InvoiceRectificativeShort",
        _ => "InvoiceSimplifiedShort",
    }];

    private (List<string>, List<IReadOnlyList<object?>>) Products()
    {
        using var db = dbFactory.CreateDbContext();
        var products = db.Products.AsNoTracking().Include(p => p.Category).Include(p => p.ParentProduct)
            .OrderBy(p => p.Name).ToList();
        // Las cinco primeras columnas son las de la plantilla de importación: el fichero se puede volver a importar.
        List<string> headers = [.. Cols("ColName", "ColPrice", "ColVat", "ColBarcode", "ColCategory", "ColVariantOf", "ColLocation", "ColActive")];
        var rows = products.Select(p => (IReadOnlyList<object?>)
            [p.Name, p.Price, p.VatRate, p.Barcode, p.Category?.Name, p.ParentProduct?.Name, p.Location, localizer[p.IsActive ? "Yes" : "No"]])
            .ToList();
        return (headers, rows);
    }

    private (List<string>, List<IReadOnlyList<object?>>) Customers()
    {
        using var db = dbFactory.CreateDbContext();
        List<string> headers = [.. Cols("ColNif", "ColCustomerName", "ColAddress", "ColPostalCode", "ColCity")];
        var rows = db.Customers.AsNoTracking().OrderBy(c => c.Name).AsEnumerable()
            .Select(c => (IReadOnlyList<object?>)[c.Nif, c.Name, c.Address, c.PostalCode, c.City])
            .ToList();
        return (headers, rows);
    }

    /// <summary>Una fila por línea vendida (o devuelta, en negativo).</summary>
    private (List<string>, List<IReadOnlyList<object?>>) Sales(DateOnly from, DateOnly to)
    {
        var (fromUtc, toUtc) = UtcRange(ref from, ref to);
        using var db = dbFactory.CreateDbContext();
        var sales = db.Sales.AsNoTracking()
            .Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc)
            .Include(s => s.Lines)
            .OrderBy(s => s.CreatedAtUtc).ThenBy(s => s.Id)
            .ToList();
        var saleIds = sales.Select(s => s.Id).ToList();
        // La factura vigente de cada venta: la completa sustituye al ticket (FAC-06).
        var invoices = db.Invoices.AsNoTracking().Where(i => saleIds.Contains(i.SaleId))
            .AsEnumerable()
            .GroupBy(i => i.SaleId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(i => i.Id).First().Code);
        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.Name);
        var categories = db.Categories.AsNoTracking().ToDictionary(c => c.Id, c => c.Name);

        List<string> headers = [.. Cols("ColDate", "ColInvoice", "ColKind", "ColCashier", "ColName", "ColCategory",
            "ColQuantity", "ColUnitPrice", "ColDiscount", "ColVat", "ColLineTotal")];
        var rows = sales.SelectMany(s => s.Lines.OrderBy(l => l.Id).Select(l => (IReadOnlyList<object?>)
            [
                s.CreatedAtUtc.ToLocalTime(),
                invoices.GetValueOrDefault(s.Id),
                localizer[s.Kind == SaleKind.Return ? "KindReturn" : "KindSale"],
                users.GetValueOrDefault(s.UserId),
                l.Description,
                l.CategoryId is { } c ? categories.GetValueOrDefault(c) : null,
                l.Quantity,
                l.UnitPrice,
                l.Discount,
                l.VatRate,
                l.LineTotal,
            ]))
            .ToList();
        return (headers, rows);
    }

    /// <summary>FAC-05: una fila por factura con base y cuota de cada tipo de IVA, y una fila final de totales.</summary>
    private (List<string>, List<IReadOnlyList<object?>>) Invoices(InvoiceBook book)
    {
        List<string> headers = [.. Cols("ColDate", "ColInvoice", "ColKind", "ColCustomerNif", "ColCustomerName", "ColRectifies", "ColReplacedBy")];
        foreach (var rate in book.Rates)
        {
            headers.Add(string.Format(localizer["ColBaseRate"], rate.ToString("0.##", localizer.Culture)));
            headers.Add(string.Format(localizer["ColVatRate"], rate.ToString("0.##", localizer.Culture)));
        }
        headers.Add(localizer["ColTotal"]);

        var rows = new List<IReadOnlyList<object?>>();
        foreach (var r in book.Rows)
        {
            var row = new List<object?> { r.IssuedAtLocal, r.Code, TypeName(r.Type), r.CustomerNif, r.CustomerName, r.RectifiesCode, r.ReplacedBy };
            foreach (var rate in book.Rates)
            {
                var (b, v) = r.ByRate.GetValueOrDefault(rate);
                row.Add(b);
                row.Add(v);
            }
            row.Add(r.Total);
            rows.Add(row);
        }

        // Los tickets sustituidos por una factura completa no suman: lo vendido ya está en la factura.
        var totals = new List<object?> { null, localizer["TotalRow"], null, null, null, null, null };
        foreach (var rate in book.Rates)
        {
            var (b, v) = book.TotalFor(rate);
            totals.Add(b);
            totals.Add(v);
        }
        totals.Add(book.Total);
        rows.Add(totals);
        return (headers, rows);
    }

    private IEnumerable<string> Cols(params string[] keys) => keys.Select(k => localizer[k]);

    private static (DateTime FromUtc, DateTime ToUtc) UtcRange(ref DateOnly from, ref DateOnly to)
    {
        if (to < from)
            (from, to) = (to, from);
        return (from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime(),
            to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime());
    }
}
