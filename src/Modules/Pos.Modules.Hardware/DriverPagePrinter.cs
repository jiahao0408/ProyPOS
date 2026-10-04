using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using Pos.Core.Hardware;

namespace Pos.Modules.Hardware;

/// <summary>
/// v2: imprime páginas (imágenes) en una impresora normal de Windows con su driver: láser y tinta (HP, Brother,
/// Canon, Epson…) y también impresoras de etiquetas con driver (Brother QL, Dymo…), donde cada imagen es una
/// etiqueta. Si una imagen no cabe en el alto del papel (un ticket largo en una etiqueta), sigue en la página siguiente.
/// </summary>
public sealed class DriverPagePrinter : IPagePrinter
{
    /// <summary>Margen propio dentro del área imprimible, en centésimas de pulgada (unos 2,5 mm).</summary>
    private const float Margin = 10;

    public Task PrintAsync(PagePrintJob job, CancellationToken cancellationToken = default) =>
        Task.Run(() => Print(job), cancellationToken);

    private static void Print(PagePrintJob job)
    {
        var images = job.PngPages.Select(png => new Bitmap(new MemoryStream(png))).ToList();
        try
        {
            using var document = new PrintDocument
            {
                DocumentName = job.DocumentName,
                PrintController = new StandardPrintController(), // sin la ventana de "Imprimiendo…"
            };
            document.PrinterSettings.PrinterName = job.PrinterName;
            if (!document.PrinterSettings.IsValid)
                throw new InvalidOperationException($"Printer '{job.PrinterName}' not found");

            var index = 0;
            var offset = 0; // filas de la imagen actual ya impresas
            document.PrintPage += (_, e) =>
            {
                var image = images[index];
                var area = e.PageSettings.PrintableArea; // centésimas de pulgada, desde el borde del papel
                var g = e.Graphics!;
                g.InterpolationMode = InterpolationMode.NearestNeighbor;
                g.PixelOffsetMode = PixelOffsetMode.Half;

                // Ancho pedido en mm (la columna del ticket, a su tamaño real) o todo el ancho imprimible.
                var maxWidth = area.Width - 2 * Margin;
                var width = Math.Min(maxWidth, job.WidthMm is { } mm ? (float)(mm / 25.4 * 100) : maxWidth);
                var unitsPerPixel = width / image.Width;
                var maxHeight = area.Height - 2 * Margin;
                var rows = Math.Min(image.Height - offset, (int)(maxHeight / unitsPerPixel));
                var x = job.WidthMm is null ? Margin : Margin + (maxWidth - width) / 2; // el ticket, centrado
                g.DrawImage(image, new RectangleF(x, Margin, width, rows * unitsPerPixel),
                    new RectangleF(0, offset, image.Width, rows), GraphicsUnit.Pixel);

                offset += rows;
                if (offset >= image.Height)
                {
                    index++;
                    offset = 0;
                }
                e.HasMorePages = index < images.Count;
            };
            document.Print();
        }
        finally
        {
            foreach (var image in images)
                image.Dispose();
        }
    }
}
