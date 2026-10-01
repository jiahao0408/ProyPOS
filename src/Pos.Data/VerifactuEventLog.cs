using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Pos.Core.Domain;

namespace Pos.Data;

/// <summary>Tipos de evento del registro de eventos (VFA-04).</summary>
public static class VerifactuEventTypes
{
    /// <summary>Arranque del sistema en la modalidad No VERI*FACTU.</summary>
    public const string Startup = "Inicio";

    public const string ModeChanged = "CambioModalidad";

    /// <summary>Exportación del registro de facturación (DAT-04).</summary>
    public const string Export = "Exportacion";

    /// <summary>Hay registros sin firmar (falta el certificado).</summary>
    public const string SignatureMissing = "AnomaliaFirma";

    /// <summary>Una verificación encontró la cadena alterada.</summary>
    public const string IntegrityAnomaly = "AnomaliaIntegridad";
}

/// <summary>
/// Añade un evento encadenado al registro de eventos, en el contexto dado (se guarda con la operación
/// que lo provoca). La huella es la SHA-256 de sus datos y de la huella del evento anterior.
/// </summary>
public static class VerifactuEventLog
{
    public static VerifactuEvent Record(PosDbContext db, string type, string details, DateTime nowUtc, string? userName = null)
    {
        var previous = db.ChangeTracker.Entries<VerifactuEvent>()
                           .Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added)
                           .Select(e => e.Entity)
                           .LastOrDefault()
                       ?? db.VerifactuEvents.OrderByDescending(e => e.Id).FirstOrDefault();
        var e = new VerifactuEvent
        {
            AtUtc = nowUtc,
            Type = type,
            Details = details.Length > 1000 ? details[..1000] : details,
            UserName = userName,
            PreviousHash = previous?.Hash,
            Hash = "",
        };
        e.Hash = HashOf(e);
        db.VerifactuEvents.Add(e);
        return e;
    }

    public static string HashOf(VerifactuEvent e) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
        $"Tipo={e.Type}&Fecha={e.AtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)}" +
        $"&Usuario={e.UserName}&Detalle={e.Details}&Huella={e.PreviousHash}")));

    /// <summary>Comprueba la cadena de eventos. Devuelve el Id del primer evento alterado, o null si está intacta.</summary>
    public static int? FirstBroken(IEnumerable<VerifactuEvent> eventsInOrder)
    {
        string? previous = null;
        foreach (var e in eventsInOrder)
        {
            if (e.PreviousHash != previous || HashOf(e) != e.Hash)
                return e.Id;
            previous = e.Hash;
        }
        return null;
    }
}
