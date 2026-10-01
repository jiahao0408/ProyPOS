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

    // --- Cierre Z: foto de los totales en el momento del cierre ---

    /// <summary>Número correlativo del cierre Z.</summary>
    public int? ZNumber { get; set; }

    /// <summary>Fondo inicial + cobros en efectivo del turno.</summary>
    public decimal? ExpectedCash { get; set; }

    public int? SalesCount { get; set; }

    public decimal? SalesTotal { get; set; }

    public decimal? CashTotal { get; set; }

    public decimal? CardTotal { get; set; }

    public bool IsOpen => ClosedAtUtc is null;

    /// <summary>Descuadre: positivo si sobra dinero, negativo si falta.</summary>
    public decimal? Difference => CountedCash - ExpectedCash;
}
