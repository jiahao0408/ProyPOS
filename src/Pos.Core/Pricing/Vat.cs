namespace Pos.Core.Pricing;

public readonly record struct VatBreakdown(decimal Base, decimal VatAmount, decimal Total);

/// <summary>Desglose de IVA a partir de precios con IVA incluido (FAC-01: base, IVA y total).</summary>
public static class Vat
{
    public static VatBreakdown FromGross(decimal total, decimal ratePercent)
    {
        if (ratePercent < 0)
            throw new ArgumentOutOfRangeException(nameof(ratePercent), "El tipo de IVA no puede ser negativo.");

        var @base = Math.Round(total / (1 + ratePercent / 100m), 2, MidpointRounding.AwayFromZero);
        // El IVA se obtiene por diferencia para que base + IVA cuadre siempre con el total cobrado.
        return new VatBreakdown(@base, total - @base, total);
    }
}
