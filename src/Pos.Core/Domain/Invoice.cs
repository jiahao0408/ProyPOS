namespace Pos.Core.Domain;

public enum InvoiceType
{
    /// <summary>Factura simplificada (el ticket). Serie T.</summary>
    Simplified = 0,

    /// <summary>Factura completa con datos del cliente. Serie F.</summary>
    Complete = 1,

    /// <summary>Factura rectificativa de una devolución (FAC-03). Serie R; importes en negativo.</summary>
    Rectificative = 2,
}

/// <summary>
/// Factura emitida (FAC-01, FAC-02, FAC-06). Es un dato de facturación: no se modifica ni se borra.
/// Guarda una copia de los datos del emisor y del cliente tal como estaban al emitirla.
/// </summary>
public class Invoice
{
    public int Id { get; set; }

    public InvoiceType Type { get; set; }

    /// <summary>Serie: letra del tipo + año, por ejemplo "T2026".</summary>
    public required string Series { get; set; }

    /// <summary>Número correlativo dentro de la serie, sin huecos.</summary>
    public int Number { get; set; }

    /// <summary>Serie y número para mostrar e imprimir: "T2026-000001".</summary>
    public required string Code { get; set; }

    public DateTime IssuedAtUtc { get; set; }

    public int SaleId { get; set; }

    public Sale? Sale { get; set; }

    public decimal Total { get; set; }

    public required string IssuerName { get; set; }

    public required string IssuerNif { get; set; }

    public required string IssuerAddress { get; set; }

    public string? CustomerNif { get; set; }

    public string? CustomerName { get; set; }

    public string? CustomerAddress { get; set; }

    /// <summary>FAC-06: factura simplificada a la que sustituye esta factura completa.</summary>
    public int? ReplacesInvoiceId { get; set; }

    /// <summary>FAC-03: en una rectificativa, la factura original que rectifica. La original nunca se borra.</summary>
    public int? RectifiedInvoiceId { get; set; }

    public List<InvoiceVatLine> VatLines { get; set; } = [];

    public static string FormatCode(string series, int number) => $"{series}-{number:D6}";
}

/// <summary>Desglose por tipo de IVA: base, cuota y total.</summary>
public class InvoiceVatLine
{
    public int Id { get; set; }

    public int InvoiceId { get; set; }

    public decimal Rate { get; set; }

    public decimal Base { get; set; }

    public decimal VatAmount { get; set; }

    public decimal Total { get; set; }
}

/// <summary>Cliente de una factura completa. Se guarda para no volver a teclearlo.</summary>
public class Customer
{
    public int Id { get; set; }

    public required string Nif { get; set; }

    public required string Name { get; set; }

    public required string Address { get; set; }

    public string PostalCode { get; set; } = "";

    public string City { get; set; } = "";
}
