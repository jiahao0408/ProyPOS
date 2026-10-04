namespace Pos.Core.Hardware;

/// <summary>
/// v2: un trabajo para una impresora normal de Windows (láser o de tinta: HP, Brother, Canon…), que se
/// imprime con su driver en vez de con comandos ESC/POS. Las páginas van ya dibujadas como imágenes PNG.
/// </summary>
/// <param name="PrinterName">Nombre de la impresora en Windows.</param>
/// <param name="PngPages">Una imagen por página lógica (un ticket, una etiqueta, una página de factura).</param>
/// <param name="WidthMm">Ancho físico con el que se imprime cada imagen; null = todo el ancho imprimible del papel.</param>
/// <param name="Dpi">Resolución con la que se dibujaron las imágenes (para su tamaño real si <paramref name="WidthMm"/> es null).</param>
public sealed record PagePrintJob(string PrinterName, IReadOnlyList<byte[]> PngPages, double? WidthMm, int Dpi, string DocumentName);

/// <summary>Imprime páginas en una impresora de Windows con su driver. Lo implementa el módulo de hardware.</summary>
public interface IPagePrinter
{
    Task PrintAsync(PagePrintJob job, CancellationToken cancellationToken = default);
}
