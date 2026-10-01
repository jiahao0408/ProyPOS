using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Pos.Core.Domain;
using Pos.Data;

namespace Pos.Modules.Verifactu;

/// <summary>
/// VFA-04: en la modalidad No VERI*FACTU cada registro se firma con el certificado del negocio (XAdES) y se
/// conserva. Sin certificado no se puede firmar: se anota la anomalía en el registro de eventos (una vez al día)
/// y se firman en cuanto se cargue.
/// </summary>
public sealed class VerifactuSigner(
    IDbContextFactory<PosDbContext> dbFactory,
    CertificateStore certificates,
    VerifactuSender sender,
    ProducerInfo producer,
    TimeProvider clock)
{
    public const int MaxPerRun = 200;

    /// <summary>Firma los registros no enviados que aún no tienen firma. Devuelve cuántos ha firmado.</summary>
    public int SignPending()
    {
        using var db = dbFactory.CreateDbContext();
        var signedIds = db.VerifactuSignatures.Select(s => s.RecordId);
        var unsigned = db.VerifactuRecords
            .Include(r => r.Invoice).ThenInclude(i => i!.VatLines)
            .Include(r => r.PreviousRecord)
            .Where(r => r.Status == VerifactuStatus.NotSent && !signedIds.Contains(r.Id))
            .OrderBy(r => r.Id)
            .Take(MaxPerRun)
            .ToList();
        if (unsigned.Count == 0)
            return 0;

        var now = clock.GetUtcNow();
        using var certificate = certificates.Load();
        if (certificate is null || !certificate.HasPrivateKey)
        {
            var since = now.UtcDateTime.AddDays(-1);
            if (!db.VerifactuEvents.Any(e => e.Type == VerifactuEventTypes.SignatureMissing && e.AtUtc > since))
            {
                VerifactuEventLog.Record(db, VerifactuEventTypes.SignatureMissing,
                    $"{unsigned.Count} registros sin firmar: falta el certificado", now.UtcDateTime);
                db.SaveChanges();
            }
            return 0;
        }

        var invoiceIds = unsigned.SelectMany(r => new[] { r.Invoice!.ReplacesInvoiceId, r.Invoice!.RectifiedInvoiceId }).OfType<int>().ToList();
        var related = db.Invoices.AsNoTracking().Where(i => invoiceIds.Contains(i.Id)).ToDictionary(i => i.Id);
        var subject = certificate.Subject.Length > 300 ? certificate.Subject[..300] : certificate.Subject;
        foreach (var r in unsigned)
        {
            var outgoing = new OutgoingRecord(r, r.Invoice!, r.PreviousRecord,
                r.Invoice!.ReplacesInvoiceId is { } id ? related.GetValueOrDefault(id) : null,
                r.Invoice!.RectifiedInvoiceId is { } rid ? related.GetValueOrDefault(rid) : null);
            string xml;
            try
            {
                xml = XadesSigner.Sign(VerifactuXml.RegistroAlta(outgoing, producer, sender.InstallationNumber), certificate, clock.GetLocalNow());
            }
            catch (CryptographicException)
            {
                return 0;
            }
            db.VerifactuSignatures.Add(new VerifactuSignature
            {
                RecordId = r.Id,
                SignedXml = xml,
                SignedAtUtc = now.UtcDateTime,
                CertificateSubject = subject,
            });
        }
        db.SaveChanges();
        return unsigned.Count;
    }

    /// <summary>Firma de un registro (null si aún no está firmado).</summary>
    public VerifactuSignature? GetSignature(int recordId)
    {
        using var db = dbFactory.CreateDbContext();
        return db.VerifactuSignatures.AsNoTracking().FirstOrDefault(s => s.RecordId == recordId);
    }

    public int UnsignedCount()
    {
        using var db = dbFactory.CreateDbContext();
        var signedIds = db.VerifactuSignatures.Select(s => s.RecordId);
        return db.VerifactuRecords.Count(r => r.Status == VerifactuStatus.NotSent && !signedIds.Contains(r.Id));
    }
}
