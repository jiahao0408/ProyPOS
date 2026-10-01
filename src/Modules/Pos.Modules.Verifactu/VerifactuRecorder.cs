using Microsoft.EntityFrameworkCore;
using Pos.Core.Domain;
using Pos.Core.Verifactu;
using Pos.Data;

namespace Pos.Modules.Verifactu;

/// <summary>
/// FAC-04: con cada factura se genera su registro de facturación encadenado, en la misma
/// transacción que la factura (si no se puede generar, la venta no se guarda).
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
            InvoiceType = TypeOf(invoice),
            TotalVat = invoice.VatLines.Sum(v => v.VatAmount),
            Total = invoice.Total,
            PreviousHash = previous?.Hash,
            GeneratedAt = VerifactuHash.Timestamp(clock.GetLocalNow()),
            Hash = "",
            Status = VerifactuStatus.Pending,
        };
        record.Hash = VerifactuHash.ForAlta(record.IssuerNif, record.InvoiceNumber, record.IssueDate, record.InvoiceType,
            record.TotalVat, record.Total, record.PreviousHash, record.GeneratedAt);
        db.VerifactuRecords.Add(record);
    }

    /// <summary>F1 completa, F2 simplificada, F3 completa que sustituye a una simplificada (FAC-06).</summary>
    public static string TypeOf(Invoice invoice) =>
        invoice.Type == InvoiceType.Simplified ? "F2"
        : invoice.ReplacesInvoiceId is not null ? "F3"
        : "F1";
}
