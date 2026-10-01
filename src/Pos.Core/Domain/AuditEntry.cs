namespace Pos.Core.Domain;

/// <summary>
/// USR-03: registro de acciones sensibles (devoluciones, descuentos, cambios de precio…).
/// Guarda usuario, fecha, acción y valores. No se puede modificar ni borrar.
/// </summary>
public class AuditEntry
{
    public int Id { get; set; }

    public DateTime AtUtc { get; set; }

    public int? UserId { get; set; }

    public required string UserName { get; set; }

    /// <summary>Usuario administrador que autorizó la acción con su PIN, si fue un cajero quien la hizo.</summary>
    public string? AuthorizedBy { get; set; }

    /// <summary>Código de la acción: "Return", "Discount", "PriceChange"…</summary>
    public required string Action { get; set; }

    /// <summary>Valores de la acción, legibles: "Venta 12 · 2 x Taza · motivo: rota".</summary>
    public required string Details { get; set; }
}
