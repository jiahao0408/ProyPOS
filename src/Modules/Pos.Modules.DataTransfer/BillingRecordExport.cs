using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pos.Core.Domain;
using Pos.Core.Verifactu;
using Pos.Data;

namespace Pos.Modules.DataTransfer;

/// <summary>
/// Resultado de comprobar una exportación del registro de facturación. Si algo falla, la clave del error
/// (null si está bien) y en el detalle, dónde.
/// </summary>
public sealed record BillingVerification(int Records, string? FinalHash, string? ErrorKey = null, string? Detail = null)
{
    public bool IsValid => ErrorKey is null;
}

/// <summary>
/// DAT-04: exporta el registro de facturación completo para una inspección o la gestoría: un ZIP con
/// todas las facturas (con su desglose de IVA), todos los registros de Verifactu con su huella, un
/// manifiesto con la SHA-256 de cada fichero y un LEEME que explica cómo comprobarlo.
/// <para>
/// Cómo se detecta una alteración: la huella de cada registro se calcula con sus datos y la huella del
/// anterior (algoritmo público de la AEAT), así que cambiar un importe rompe esa huella y todas las
/// siguientes. Para "arreglarlo" habría que recalcular toda la cadena, y la huella final no coincidiría
/// con la de la base de datos ni con la que ya tiene la AEAT. <see cref="Verify"/> hace las tres comprobaciones.
/// </para>
/// </summary>
public sealed class BillingRecordExport(IDbContextFactory<PosDbContext> dbFactory, TimeProvider clock)
{
    public const string RecordsFile = "registros.csv";
    public const string InvoicesFile = "facturas.csv";
    public const string EventsFile = "eventos.csv";
    public const string ManifestFile = "manifiesto.txt";
    public const string ReadmeFile = "LEEME.txt";

    private static readonly string[] RecordColumns =
    [
        "Orden", "Tipo", "IDEmisorFactura", "NombreRazonEmisor", "NumSerieFactura", "FechaExpedicionFactura", "TipoFactura",
        "CuotaTotal", "ImporteTotal", "HuellaAnterior", "FechaHoraHusoGenRegistro", "Huella", "EstadoEnvio", "Entorno", "CodigoError",
    ];

    private static readonly string[] EventColumns = ["Id", "Fecha", "Tipo", "Usuario", "Detalle", "HuellaAnterior", "Huella"];

    private static readonly string[] InvoiceColumns =
    [
        "NumSerieFactura", "FechaHoraExpedicion", "Clase", "IDEmisorFactura", "NIFCliente", "NombreCliente", "Rectifica",
        "TipoImpositivo", "BaseImponible", "CuotaRepercutida", "Total", "TotalFactura",
    ];

    public string SuggestedFileName => $"registro-facturacion-{clock.GetLocalNow():yyyyMMdd-HHmm}.zip";

    /// <summary>Escribe el ZIP. Devuelve el número de registros y la huella final (para anotarla).</summary>
    public BillingVerification Export(string path)
    {
        using var db = dbFactory.CreateDbContext();
        // VFA-04: la exportación queda en el registro de eventos (y va incluida en el propio fichero).
        VerifactuEventLog.Record(db, VerifactuEventTypes.Export, $"Registro de facturación exportado: {Path.GetFileName(path)}",
            clock.GetUtcNow().UtcDateTime);
        db.SaveChanges();
        var records = db.VerifactuRecords.AsNoTracking().OrderBy(r => r.Id).ToList();
        var events = db.VerifactuEvents.AsNoTracking().OrderBy(e => e.Id).ToList();
        var invoices = db.Invoices.AsNoTracking().Include(i => i.VatLines).OrderBy(i => i.Id).ToList();
        var codes = invoices.ToDictionary(i => i.Id, i => i.Code);

        var recordsCsv = TabularFile.ToCsv(RecordColumns, records.Select((r, i) => (IReadOnlyList<string>)
        [
            (i + 1).ToString(CultureInfo.InvariantCulture), r.Kind.ToString(), r.IssuerNif, r.IssuerName, r.InvoiceNumber, r.IssueDate,
            r.InvoiceType, VerifactuHash.Amount(r.TotalVat), VerifactuHash.Amount(r.Total), r.PreviousHash ?? "", r.GeneratedAt, r.Hash,
            r.Status.ToString(), r.Environment ?? "", r.ErrorCode ?? "",
        ]));

        var invoicesCsv = TabularFile.ToCsv(InvoiceColumns, invoices.SelectMany(i => i.VatLines.OrderByDescending(v => v.Rate).Select(v => (IReadOnlyList<string>)
        [
            i.Code, i.IssuedAtUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), i.Type.ToString(), i.IssuerNif,
            i.CustomerNif ?? "", i.CustomerName ?? "", i.RectifiedInvoiceId is { } r ? codes.GetValueOrDefault(r, "") : "",
            VerifactuHash.Amount(v.Rate), VerifactuHash.Amount(v.Base), VerifactuHash.Amount(v.VatAmount), VerifactuHash.Amount(v.Total),
            VerifactuHash.Amount(i.Total),
        ])));

        var eventsCsv = TabularFile.ToCsv(EventColumns, events.Select(e => (IReadOnlyList<string>)
        [
            e.Id.ToString(CultureInfo.InvariantCulture), e.AtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            e.Type, e.UserName ?? "", e.Details, e.PreviousHash ?? "", e.Hash,
        ]));

        var finalHash = records.LastOrDefault()?.Hash;
        var files = new Dictionary<string, string>
        {
            [RecordsFile] = recordsCsv,
            [InvoicesFile] = invoicesCsv,
            [EventsFile] = eventsCsv,
            [ReadmeFile] = Readme(records.Count, finalHash),
        };

        var manifest = new StringBuilder();
        manifest.AppendLine($"StarSeaPOS - registro de facturación exportado el {clock.GetLocalNow():yyyy-MM-dd HH:mm:ss zzz}");
        manifest.AppendLine($"Registros: {records.Count}");
        manifest.AppendLine($"Facturas: {invoices.Count}");
        manifest.AppendLine($"HuellaFinal: {finalHash}");
        foreach (var (name, text) in files)
            manifest.AppendLine($"SHA256 {Sha256(text)} {name}");

        if (File.Exists(path))
            File.Delete(path);
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            foreach (var (name, text) in files.Append(new(ManifestFile, manifest.ToString())))
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }
        return new BillingVerification(records.Count, finalHash);
    }

    /// <summary>
    /// Comprueba una exportación: que los ficheros no han cambiado (manifiesto), que cada huella es correcta
    /// y encadena con la anterior, que los importes coinciden con las facturas y que la cadena es la misma
    /// que la de esta base de datos.
    /// </summary>
    public BillingVerification Verify(string path)
    {
        var result = Check(path);
        if (!result.IsValid && result.ErrorKey != "ErrorExportInvalid")
        {
            // VFA-04: una alteración detectada es una anomalía de integridad.
            using var db = dbFactory.CreateDbContext();
            VerifactuEventLog.Record(db, VerifactuEventTypes.IntegrityAnomaly,
                $"{Path.GetFileName(path)}: {result.ErrorKey} {result.Detail}", clock.GetUtcNow().UtcDateTime);
            db.SaveChanges();
        }
        return result;
    }

    private BillingVerification Check(string path)
    {
        Dictionary<string, string> files;
        try
        {
            using var zip = ZipFile.OpenRead(path);
            files = zip.Entries.ToDictionary(e => e.FullName, e =>
            {
                using var reader = new StreamReader(e.Open(), Encoding.UTF8);
                return reader.ReadToEnd();
            });
        }
        catch (Exception e) when (e is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            return new BillingVerification(0, null, "ErrorExportInvalid");
        }
        if (!files.TryGetValue(ManifestFile, out var manifest) || !files.TryGetValue(RecordsFile, out var recordsCsv)
            || !files.TryGetValue(InvoicesFile, out var invoicesCsv))
            return new BillingVerification(0, null, "ErrorExportInvalid");

        // 1. Ningún fichero ha cambiado desde que se exportó.
        foreach (var line in manifest.Split('\n').Select(l => l.Trim()).Where(l => l.StartsWith("SHA256 ", StringComparison.Ordinal)))
        {
            var parts = line.Split(' ', 3);
            if (!files.TryGetValue(parts[2], out var text) || Sha256(text) != parts[1])
                return new BillingVerification(0, null, "ErrorExportFileChanged", parts[2]);
        }

        // 2. Cada huella se recalcula con sus datos y encadena con la anterior.
        var rows = TabularFile.ParseCsv(recordsCsv, ';').Skip(1).Where(r => r.Length >= RecordColumns.Length).ToList();
        string? previous = null;
        foreach (var r in rows)
        {
            var (number, issuerNif, invoice, date, type, previousHash, generated, hash) = (r[0], r[2], r[4], r[5], r[6], r[9], r[10], r[11]);
            var expected = r[1] == nameof(VerifactuRecordKind.Anulacion)
                ? VerifactuHash.ForAnulacion(issuerNif, invoice, date, previousHash, generated)
                : VerifactuHash.ForAlta(issuerNif, invoice, date, type, ParseAmount(r[7]), ParseAmount(r[8]), previousHash, generated);
            if (previousHash != (previous ?? ""))
                return new BillingVerification(rows.Count, null, "ErrorExportChainBroken", $"{number} · {invoice}");
            if (expected != hash)
                return new BillingVerification(rows.Count, null, "ErrorExportHashMismatch", $"{number} · {invoice}");
            previous = hash;
        }
        var finalLine = manifest.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith("HuellaFinal:", StringComparison.Ordinal));
        if ((finalLine?["HuellaFinal:".Length..].Trim() ?? "") != (previous ?? ""))
            return new BillingVerification(rows.Count, previous, "ErrorExportChainBroken", "HuellaFinal");

        // El registro de eventos también está encadenado.
        if (files.TryGetValue(EventsFile, out var eventsCsv))
        {
            var events = TabularFile.ParseCsv(eventsCsv, ';').Skip(1).Where(r => r.Length >= EventColumns.Length)
                .Select(r => new VerifactuEvent
                {
                    Id = int.TryParse(r[0], out var id) ? id : 0,
                    AtUtc = DateTime.TryParseExact(r[1], "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal, out var at) ? at : default,
                    Type = r[2],
                    UserName = r[3].Length == 0 ? null : r[3],
                    Details = r[4],
                    PreviousHash = r[5].Length == 0 ? null : r[5],
                    Hash = r[6],
                });
            if (VerifactuEventLog.FirstBroken(events) is { } broken)
                return new BillingVerification(rows.Count, previous, "ErrorExportEventsBroken", broken.ToString(CultureInfo.InvariantCulture));
        }

        // 3. Los importes de los registros de alta son los de las facturas.
        var totals = TabularFile.ParseCsv(invoicesCsv, ';').Skip(1).Where(r => r.Length >= InvoiceColumns.Length)
            .GroupBy(r => r[0])
            .ToDictionary(g => g.Key, g => (Vat: g.Sum(r => ParseAmount(r[9])), Total: ParseAmount(g.First()[11])));
        foreach (var r in rows.Where(r => r[1] == nameof(VerifactuRecordKind.Alta)))
        {
            if (!totals.TryGetValue(r[4], out var t) || t.Vat != ParseAmount(r[7]) || t.Total != ParseAmount(r[8]))
                return new BillingVerification(rows.Count, previous, "ErrorExportAmountsDiffer", $"{r[0]} · {r[4]}");
        }

        // 4. La cadena es la de esta base de datos (una copia recalculada entera no coincidiría).
        using var db = dbFactory.CreateDbContext();
        var stored = db.VerifactuRecords.AsNoTracking().OrderBy(x => x.Id).Select(x => x.Hash).Take(rows.Count).ToList();
        for (var i = 0; i < Math.Min(stored.Count, rows.Count); i++)
        {
            if (stored[i] != rows[i][11])
                return new BillingVerification(rows.Count, previous, "ErrorExportDiffersFromDatabase", $"{rows[i][0]} · {rows[i][4]}");
        }

        return new BillingVerification(rows.Count, previous);
    }

    private static decimal ParseAmount(string text) =>
        decimal.TryParse(text, NumberStyles.Number | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : decimal.MinValue;

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    private static string Readme(int records, string? finalHash) => $"""
        REGISTRO DE FACTURACIÓN - StarSeaPOS

        Contenido
          {RecordsFile}   Registros de facturación de Verifactu, en el orden en que se generaron, con su huella.
          {InvoicesFile}    Facturas emitidas, una fila por tipo de IVA (base, cuota y total).
          {EventsFile}     Registro de eventos del sistema (arranques, cambios de modalidad, exportaciones, anomalías), encadenado.
          {ManifestFile}  SHA-256 de cada fichero y huella final de la cadena.

        Registros: {records}
        Huella final: {finalHash}

        Cómo comprobar que nada se ha alterado
          1. La SHA-256 de cada fichero coincide con la del manifiesto.
          2. La huella de cada registro es la SHA-256 (hexadecimal, mayúsculas) de la cadena
             IDEmisorFactura=…&NumSerieFactura=…&FechaExpedicionFactura=…&TipoFactura=…&CuotaTotal=…&ImporteTotal=…&Huella=<huella anterior>&FechaHoraHusoGenRegistro=…
             (Orden HAC/1177/2024 y especificación técnica de Verifactu de la AEAT). Cada registro lleva la
             huella del anterior: cambiar o borrar un registro rompe la huella de todos los siguientes.
          3. La huella final coincide con la que guarda la aplicación y con los registros enviados a la AEAT.
        StarSeaPOS hace estas comprobaciones en Datos y copias → Verificar exportación.

        Importes con punto decimal. Fechas de expedición en formato dd-MM-yyyy.
        """;
}
