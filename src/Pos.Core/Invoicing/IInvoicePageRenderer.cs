namespace Pos.Core.Invoicing;

/// <summary>
/// v2: la factura en páginas A4 (PNG), para imprimirla en una impresora normal. Lo implementa el módulo de
/// facturación con el mismo diseño que el PDF.
/// </summary>
public interface IInvoicePageRenderer
{
    /// <summary>Resolución de las imágenes que devuelve.</summary>
    int Dpi { get; }

    IReadOnlyList<byte[]> RenderPages(InvoiceDocument doc, string? language = null);
}
