namespace Pos.Core.Domain;

public enum VerifactuRecordKind
{
    Alta = 0,
    Anulacion = 1,
}

public enum VerifactuStatus
{
    /// <summary>En la cola, pendiente de enviar (o de reintentar tras un fallo de conexión).</summary>
    Pending = 0,
    Accepted = 1,
    AcceptedWithErrors = 2,
    Rejected = 3,

    /// <summary>
    /// VFA-04: generado en la modalidad No VERI*FACTU. No se envía: se firma y se conserva en el sistema
    /// para entregarlo si la AEAT lo pide.
    /// </summary>
    NotSent = 4,
}

/// <summary>
/// Registro de facturación de Verifactu (FAC-04, VFA-02, VFA-03). Los datos del registro y su huella
/// no se pueden cambiar ni borrar (triggers en la BD); solo cambia el estado del envío a la AEAT.
/// </summary>
public class VerifactuRecord
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }

    public Invoice? Invoice { get; set; }

    public VerifactuRecordKind Kind { get; set; }

    public required string IssuerNif { get; set; }

    public required string IssuerName { get; set; }

    /// <summary>NumSerieFactura: serie y número, "T2026-000001".</summary>
    public required string InvoiceNumber { get; set; }

    /// <summary>FechaExpedicionFactura en formato dd-MM-yyyy.</summary>
    public required string IssueDate { get; set; }

    /// <summary>TipoFactura: F1 (completa), F2 (simplificada), F3 (completa que sustituye a simplificadas).</summary>
    public required string InvoiceType { get; set; }

    public decimal TotalVat { get; set; }

    public decimal Total { get; set; }

    /// <summary>Huella del registro anterior de la cadena; null en el primero.</summary>
    public string? PreviousHash { get; set; }

    public int? PreviousRecordId { get; set; }

    public VerifactuRecord? PreviousRecord { get; set; }

    /// <summary>FechaHoraHusoGenRegistro: 2026-10-01T11:00:00+02:00.</summary>
    public required string GeneratedAt { get; set; }

    public required string Hash { get; set; }

    /// <summary>VFA-05: registro de subsanación que vuelve a enviar este otro (rechazado o aceptado con errores).</summary>
    public int? CorrectsRecordId { get; set; }

    /// <summary>VFA-05: el registro que se subsana fue rechazado (RechazoPrevio en el XML).</summary>
    public bool PreviouslyRejected { get; set; }

    // --- Estado del envío (lo único que cambia) ---

    public VerifactuStatus Status { get; set; }

    public int Attempts { get; set; }

    public DateTime? LastAttemptUtc { get; set; }

    public DateTime? AnsweredAtUtc { get; set; }

    public string? ErrorCode { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>"Test" o "Production": a qué entorno de la AEAT se envió.</summary>
    public string? Environment { get; set; }
}
