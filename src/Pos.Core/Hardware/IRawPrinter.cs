namespace Pos.Core.Hardware;

/// <summary>Qué impresora: la de tickets o la de etiquetas (puede ser la misma).</summary>
public enum PrinterDestination
{
    Receipt = 0,
    Labels = 1,

    /// <summary>v1.1: el cajón. Por defecto va por la impresora de tickets; puede tener su propio puerto COM.</summary>
    Drawer = 2,
}

/// <summary>
/// Envía bytes tal cual (ESC/POS) a la impresora configurada (HW-01).
/// Lo implementa el módulo de hardware; la impresión de tickets solo genera los bytes.
/// </summary>
public interface IRawPrinter
{
    Task PrintAsync(byte[] data, CancellationToken cancellationToken = default) =>
        PrintAsync(data, PrinterDestination.Receipt, cancellationToken);

    Task PrintAsync(byte[] data, PrinterDestination destination, CancellationToken cancellationToken = default);
}
