namespace Pos.Core.Printing;

public sealed record ZReportVatLine(decimal Rate, decimal Base, decimal VatAmount, decimal Total);

/// <summary>Cierre Z (CAJ-02), listo para imprimir.</summary>
public sealed record ZReportDocument(
    int ZNumber,
    DateTime OpenedAtUtc,
    DateTime ClosedAtUtc,
    string ClosedBy,
    int SalesCount,
    decimal SalesTotal,
    decimal CashTotal,
    decimal CardTotal,
    decimal OpeningFloat,
    decimal ExpectedCash,
    decimal CountedCash,
    IReadOnlyList<ZReportVatLine> VatLines)
{
    public decimal Difference => CountedCash - ExpectedCash;
}

/// <summary>Etiqueta de precio (BAZ-01): nombre, precio y código de barras.</summary>
public sealed record LabelItem(string Name, decimal Price, string Barcode, int Copies);
