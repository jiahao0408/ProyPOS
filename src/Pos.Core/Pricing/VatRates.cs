namespace Pos.Core.Pricing;

/// <summary>Tipos de IVA vigentes en España (general, reducido, superreducido y exento).</summary>
public static class VatRates
{
    public static IReadOnlyList<decimal> All { get; } = [21m, 10m, 4m, 0m];

    public static bool IsValid(decimal rate) => All.Contains(rate);
}
