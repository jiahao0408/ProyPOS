using Pos.Core.Domain;
using Pos.Core.Invoicing;

namespace Pos.Data;

/// <summary>Venta que se está cobrando, con el cliente si pide factura completa (FAC-02).</summary>
public sealed record CheckoutContext(Sale Sale, InvoiceCustomer? Customer, int UserId, DateTime NowUtc);

/// <summary>
/// Punto de extensión de la facturación: se llama con cada factura nueva (al cobrar o al facturar un
/// ticket), dentro de la misma transacción. Lo usa Verifactu para generar el registro con su huella.
/// </summary>
public interface IInvoiceHook
{
    void OnInvoiceIssued(PosDbContext db, Invoice invoice);
}

/// <summary>
/// Punto de extensión del cobro: otros módulos (facturación, y más adelante Verifactu) añaden
/// sus datos a la venta dentro de la misma transacción, sin que el módulo de ventas los conozca.
/// </summary>
public interface ISaleHook
{
    /// <summary>Se llama antes de guardar nada. Devuelve una clave de error para impedir el cobro.</summary>
    string? Validate(CheckoutContext context);

    /// <summary>Añade sus datos al contexto; se guardan junto con la venta.</summary>
    void OnSaleCreated(PosDbContext db, CheckoutContext context);
}
