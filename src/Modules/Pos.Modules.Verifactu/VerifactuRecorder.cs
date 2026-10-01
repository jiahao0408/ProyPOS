using Microsoft.EntityFrameworkCore;
using Pos.Core.Domain;
using Pos.Core.Verifactu;
using Pos.Data;

namespace Pos.Modules.Verifactu;

/// <summary>
/// FAC-04: con cada factura se genera su registro de facturación encadenado, en la misma
/// transacción que la factura (si no se puede generar, la venta no se guarda).
/// VFA-04: en VERI*FACTU queda pendiente de enviar; en No VERI*FACTU no se envía: se firma después
/// (<see cref="VerifactuSigner"/>) y se conserva.
/// </summary>
public sealed class VerifactuRecorder(TimeProvider clock) : IInvoiceHook
{
    public void OnInvoiceIssued(PosDbContext db, Invoice invoice)
    {
        // Último registro de la cadena: uno pendiente en esta misma transacción o el guardado más reciente.
        var previous = db.ChangeTracker.Entries<VerifactuRecord>()
                           .Where(e => e.State == EntityState.Added)
                           .Select(e => e.Entity)
                           .LastOrDefault()
                       ?? db.VerifactuRecords.OrderByDescending(r => r.Id).FirstOrDefault();

        var record = new VerifactuRecord
        {
            Invoice = invoice,
            PreviousRecord = previous,
            Kind = VerifactuRecordKind.Alta,
            IssuerNif = invoice.IssuerNif,
            IssuerName = invoice.IssuerName,
            InvoiceNumber = invoice.Code,
            IssueDate = VerifactuHash.Date(invoice.IssuedAtUtc.ToLocalTime()),
            InvoiceType = TypeOf(invoice, invoice.RectifiedInvoiceId is { } id ? db.Invoices.Find(id) : null),
            TotalVat = invoice.VatLines.Sum(v => v.VatAmount),
            Total = invoice.Total,
            PreviousHash = previous?.Hash,
            GeneratedAt = VerifactuHash.Timestamp(clock.GetLocalNow()),
            Hash = "",
            Status = db.Settings.Find(VerifactuSettingKeys.Enabled)?.Value == "true" ? VerifactuStatus.Pending : VerifactuStatus.NotSent,
        };
        record.Hash = VerifactuHash.ForAlta(record.IssuerNif, record.InvoiceNumber, record.IssueDate, record.InvoiceType,
            record.TotalVat, record.Total, record.PreviousHash, record.GeneratedAt);
        db.VerifactuRecords.Add(record);
    }

    /// <summary>
    /// F1 completa, F2 simplificada, F3 completa que sustituye a una simplificada (FAC-06).
    /// Rectificativas (FAC-03): R5 si rectifica una simplificada; R1 (art. 80.1 y 80.2 LIVA, que incluye las
    /// devoluciones) si rectifica una completa.
    /// </summary>
    public static string TypeOf(Invoice invoice, Invoice? rectified = null) => invoice.Type switch
    {
        InvoiceType.Simplified => "F2",
        InvoiceType.Rectificative => rectified?.Type == InvoiceType.Simplified ? "R5" : "R1",
        _ => invoice.ReplacesInvoiceId is not null ? "F3" : "F1",
    };
}
