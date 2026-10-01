using Pos.Core.Domain;

namespace Pos.Core.Invoicing;

/// <summary>Datos fiscales del cliente de una factura completa (FAC-02).</summary>
public sealed record InvoiceCustomer(string Nif, string Name, string Address, string PostalCode, string City)
{
    public string FullAddress => string.Join(", ", new[] { Address, $"{PostalCode} {City}".Trim() }.Where(s => s.Length > 0));
}

/// <param name="Discount">Descuento aplicado a la línea (VEN-05), ya restado de <paramref name="Total"/>.</param>
public sealed record InvoiceDocumentLine(string Description, int Quantity, decimal UnitPrice, decimal VatRate, decimal Total, decimal Discount = 0)
{
    /// <summary>Lo que se ha cobrado (o devuelto) por unidad, con descuentos.</summary>
    public decimal EffectiveUnitPrice => Quantity == 0 ? UnitPrice : Math.Round(Total / Quantity, 2, MidpointRounding.AwayFromZero);
}

public sealed record InvoiceDocumentPayment(PaymentMethod Method, decimal Amount);

/// <summary>
/// Todo lo que hace falta para imprimir un ticket o una factura, sin depender de la BD.
/// Lo genera el módulo de facturación y lo usan la impresora (IMP) y el PDF.
/// </summary>
public sealed record InvoiceDocument(
    int InvoiceId,
    int SaleId,
    InvoiceType Type,
    string Code,
    DateTime IssuedAtUtc,
    string IssuerName,
    string IssuerNif,
    string IssuerAddress,
    string? CustomerNif,
    string? CustomerName,
    string? CustomerAddress,
    string? ReplacesCode,
    string CashierName,
    IReadOnlyList<InvoiceDocumentLine> Lines,
    IReadOnlyList<InvoiceVatLine> VatLines,
    IReadOnlyList<InvoiceDocumentPayment> Payments,
    decimal Total,
    decimal CashTendered,
    decimal Change,
    string? QrUrl = null,
    bool VerifactuMode = false,
    string? RectifiesCode = null,
    string? ReturnReason = null);

/// <summary>Acceso a los documentos de factura para imprimirlos; lo implementa el módulo de facturación.</summary>
public interface IInvoiceDocuments
{
    InvoiceDocument? Get(int invoiceId);

    /// <summary>La factura vigente de una venta: la completa si sustituyó a la simplificada.</summary>
    InvoiceDocument? GetCurrentForSale(int saleId);
}
