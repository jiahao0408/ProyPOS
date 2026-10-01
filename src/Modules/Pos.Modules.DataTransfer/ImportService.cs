using Microsoft.EntityFrameworkCore;
using Pos.Core.Domain;
using Pos.Core.Invoicing;
using Pos.Core.Localization;
using Pos.Core.Pricing;
using Pos.Data;
using Pos.Localization;

namespace Pos.Modules.DataTransfer;

public enum ImportKind
{
    Products,
    Customers,
}

/// <summary>Una fila del fichero: su número de línea, sus valores y, si no se puede importar, el motivo.</summary>
public sealed record ImportRow(int Line, IReadOnlyList<string> Values, string? ErrorKey)
{
    public bool IsValid => ErrorKey is null;
}

public sealed record ImportPreview(ImportKind Kind, IReadOnlyList<string> Headers, IReadOnlyList<ImportRow> Rows)
{
    public int ValidCount => Rows.Count(r => r.IsValid);

    public int ErrorCount => Rows.Count(r => !r.IsValid);
}

/// <summary>
/// DAT-01: importar productos y clientes desde CSV o Excel. Plantilla descargable; vista previa con
/// errores por fila; los códigos (o NIF) duplicados no se importan. Las columnas van en el orden de la plantilla.
/// </summary>
public sealed class ImportService(IDbContextFactory<PosDbContext> dbFactory, ILocalizer localizer, RegionFormatter formatter)
{
    private static readonly string[] ProductColumns =
        ["ColName", "ColPrice", "ColVat", "ColBarcode", "ColCategory"];

    private static readonly string[] CustomerColumns =
        ["ColNif", "ColCustomerName", "ColAddress", "ColPostalCode", "ColCity"];

    public IReadOnlyList<string> Headers(ImportKind kind) =>
        (kind == ImportKind.Products ? ProductColumns : CustomerColumns).Select(k => localizer[k]).ToList();

    /// <summary>Plantilla con los encabezados y una fila de ejemplo.</summary>
    public void WriteTemplate(ImportKind kind, string path)
    {
        IReadOnlyList<string> example = kind == ImportKind.Products
            ? ["Taza de cerámica", "3,50", "21", "8410000000011", "Hogar"]
            : ["B12345674", "Papelería Pérez S.L.", "Calle Sol 5", "08001", "Barcelona"];
        TabularFile.Write(path, Headers(kind), [example]);
    }

    public ImportPreview Preview(ImportKind kind, string path)
    {
        var table = TabularFile.Read(path);
        var columns = kind == ImportKind.Products ? ProductColumns.Length : CustomerColumns.Length;
        var rows = table.Skip(1) // encabezados
            .Select((cells, i) => (Line: i + 2, Values: (IReadOnlyList<string>)Pad(cells, columns)))
            .ToList();

        using var db = dbFactory.CreateDbContext();
        var checkedRows = kind == ImportKind.Products ? ValidateProducts(db, rows) : ValidateCustomers(db, rows);
        return new ImportPreview(kind, Headers(kind), checkedRows);
    }

    /// <summary>Importa las filas válidas en una sola transacción. Devuelve cuántas se han importado.</summary>
    public int Import(ImportPreview preview)
    {
        using var db = dbFactory.CreateDbContext();
        // Se vuelve a validar: entre la vista previa y la importación pueden haber cambiado los datos.
        var rows = preview.Rows.Select(r => (r.Line, r.Values)).ToList();
        var valid = (preview.Kind == ImportKind.Products ? ValidateProducts(db, rows) : ValidateCustomers(db, rows))
            .Where(r => r.IsValid).ToList();

        using var transaction = db.Database.BeginTransaction();
        if (preview.Kind == ImportKind.Products)
        {
            ImportProducts(db, valid);
        }
        else
            ImportCustomers(db, valid);
        db.SaveChanges();
        transaction.Commit();
        return valid.Count;
    }

    // --- Productos ---

    private List<ImportRow> ValidateProducts(PosDbContext db, IReadOnlyList<(int Line, IReadOnlyList<string> Values)> rows)
    {
        var existing = db.Products.Where(p => p.Barcode != null).Select(p => p.Barcode!).ToHashSet();
        var seen = new HashSet<string>();
        var result = new List<ImportRow>();

        foreach (var (line, v) in rows)
        {
            string? error = null;
            var barcode = v[3];
            if (v[0].Length == 0)
                error = "ErrorNameRequired";
            else if (!formatter.TryParseAmount(v[1], out var price) || price < 0 || decimal.Round(price, 2) != price)
                error = "ErrorAmountFormat";
            else if (!TryParseVat(v[2], out _))
                error = "ErrorVatRate";
            else if (barcode.Length > 0 && existing.Contains(barcode))
                error = "ErrorBarcodeTaken";
            else if (barcode.Length > 0 && !seen.Add(barcode))
                error = "ErrorDuplicateInFile";
            result.Add(new ImportRow(line, v, error));
        }
        return result;
    }

    private void ImportProducts(PosDbContext db, IEnumerable<ImportRow> rows)
    {
        var categories = db.Categories.ToList().ToDictionary(c => c.Name, StringComparer.CurrentCultureIgnoreCase);

        foreach (var row in rows)
        {
            var v = row.Values;
            Category? category = null;
            if (v[4].Length > 0 && !categories.TryGetValue(v[4], out category))
            {
                category = new Category { Name = v[4], SortOrder = categories.Count };
                db.Categories.Add(category);
                categories[v[4]] = category;
            }

            formatter.TryParseAmount(v[1], out var price);
            TryParseVat(v[2], out var vat);

            db.Products.Add(new Product
            {
                Name = v[0],
                Price = price,
                VatRate = vat,
                Barcode = v[3].Length > 0 ? v[3] : null,
                Category = category,
            });
        }
    }

    // --- Clientes ---

    private static List<ImportRow> ValidateCustomers(PosDbContext db, IReadOnlyList<(int Line, IReadOnlyList<string> Values)> rows)
    {
        var existing = db.Customers.Select(c => c.Nif).ToHashSet();
        var seen = new HashSet<string>();
        var result = new List<ImportRow>();

        foreach (var (line, v) in rows)
        {
            var nif = NifValidator.Normalize(v[0]);
            string? error =
                !NifValidator.IsValid(nif) ? "ErrorCustomerNif"
                : v[1].Length == 0 ? "ErrorCustomerNameRequired"
                : v[2].Length == 0 ? "ErrorCustomerAddressRequired"
                : existing.Contains(nif) ? "ErrorNifTaken"
                : !seen.Add(nif) ? "ErrorDuplicateInFile"
                : null;
            result.Add(new ImportRow(line, v, error));
        }
        return result;
    }

    private static void ImportCustomers(PosDbContext db, IEnumerable<ImportRow> rows)
    {
        foreach (var v in rows.Select(r => r.Values))
        {
            db.Customers.Add(new Customer
            {
                Nif = NifValidator.Normalize(v[0]),
                Name = v[1],
                Address = v[2],
                PostalCode = v[3],
                City = v[4],
            });
        }
    }

    // --- Ayudantes ---

    private static string[] Pad(string[] cells, int columns) =>
        Enumerable.Range(0, columns).Select(i => i < cells.Length ? cells[i].Trim() : "").ToArray();

    /// <summary>Acepta "21", "21%", "21,0" y también 0,21 (porcentaje de Excel).</summary>
    private bool TryParseVat(string text, out decimal rate)
    {
        rate = 0;
        if (!formatter.TryParseAmount(text.Replace("%", "").Trim(), out var value))
            return false;
        if (value is > 0 and < 1)
            value *= 100;
        rate = Math.Round(value, 2);
        return VatRates.IsValid(rate);
    }
}
