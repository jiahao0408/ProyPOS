namespace Pos.Core.Domain;

/// <summary>Turno de caja: de la apertura con fondo inicial (CAJ-01) al cierre Z con arqueo (CAJ-02).</summary>
public class CashSession
{
    public int Id { get; set; }

    public int OpenedByUserId { get; set; }

    public DateTime OpenedAtUtc { get; set; }

    public decimal OpeningFloat { get; set; }

    public int? ClosedByUserId { get; set; }

    public DateTime? ClosedAtUtc { get; set; }

    /// <summary>Efectivo contado en el arqueo.</summary>
    public decimal? CountedCash { get; set; }

    public bool IsOpen => ClosedAtUtc is null;
}
