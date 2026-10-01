namespace Pos.Core.Domain;

/// <summary>
/// VFA-04: firma electrónica de un registro generado en la modalidad No VERI*FACTU. Guarda el
/// RegistroAlta firmado tal como se entregaría a la AEAT. No se modifica ni se borra.
/// </summary>
public class VerifactuSignature
{
    public int Id { get; set; }

    public int RecordId { get; set; }

    public VerifactuRecord? Record { get; set; }

    /// <summary>XML del registro con la firma (XAdES enveloped).</summary>
    public required string SignedXml { get; set; }

    public DateTime SignedAtUtc { get; set; }

    public required string CertificateSubject { get; set; }
}

/// <summary>
/// VFA-04: registro de eventos del sistema de facturación (obligatorio en No VERI*FACTU): inicio,
/// cambio de modalidad, exportaciones y anomalías. Cada evento lleva la huella del anterior, como los
/// registros de facturación, y no se puede modificar ni borrar.
/// </summary>
public class VerifactuEvent
{
    public int Id { get; set; }

    public DateTime AtUtc { get; set; }

    public required string Type { get; set; }

    public required string Details { get; set; }

    public string? UserName { get; set; }

    public string? PreviousHash { get; set; }

    public required string Hash { get; set; }
}
