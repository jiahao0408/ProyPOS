using Pos.Core.Domain;
using Pos.Core.Pricing;

namespace Pos.Modules.Sales;

/// <summary>Lo que se vende en una línea: un producto del catálogo o un artículo genérico de sección.</summary>
public sealed record TicketItem(int? ProductId, string Description, int? CategoryId, decimal UnitPrice, decimal VatRate)
{
    /// <summary>IVA de los artículos genéricos (BAZ-02): el tipo general.</summary>
    public const decimal GenericVatRate = 21m;

    public static TicketItem FromProduct(Product product) =>
        new(product.Id, product.Name, product.CategoryId, product.Price, product.VatRate);

    public static TicketItem Generic(Category section, decimal amount) =>
        new(null, section.Name, section.Id, amount, GenericVatRate);
}

public sealed class TicketLine(TicketItem item, int quantity)
{
    public TicketItem Item { get; } = item;

    public int Quantity { get; internal set; } = quantity;

    public decimal Total => Math.Round(Item.UnitPrice * Quantity, 2, MidpointRounding.AwayFromZero);
}

public sealed record TicketVatLine(decimal Rate, decimal Base, decimal VatAmount, decimal Total);

/// <summary>
/// Ticket en curso, antes de cobrar. Vive solo en memoria: quitar una línea no deja
/// rastro de venta (VEN-02). Solo al cobrar se convierte en una <see cref="Sale"/>.
/// </summary>
public sealed class Ticket
{
    private readonly List<TicketLine> _lines = [];

    public IReadOnlyList<TicketLine> Lines => _lines;

    public bool IsEmpty => _lines.Count == 0;

    public decimal Total => _lines.Sum(l => l.Total);

    public int ItemCount => _lines.Sum(l => l.Quantity);

    /// <summary>
    /// Añade unidades. Un mismo producto se acumula en una sola línea; los artículos
    /// genéricos van siempre en líneas separadas.
    /// </summary>
    public TicketLine Add(TicketItem item, int quantity = 1)
    {
        if (quantity <= 0)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        var existing = item.ProductId is null ? null : _lines.FirstOrDefault(l => l.Item == item);
        if (existing is not null)
        {
            existing.Quantity += quantity;
            return existing;
        }

        var line = new TicketLine(item, quantity);
        _lines.Add(line);
        return line;
    }

    /// <summary>Cambia la cantidad; 0 o menos quita la línea.</summary>
    public void SetQuantity(TicketLine line, int quantity)
    {
        if (quantity <= 0)
            _lines.Remove(line);
        else
            line.Quantity = quantity;
    }

    public void Remove(TicketLine line) => _lines.Remove(line);

    public void Clear() => _lines.Clear();

    /// <summary>Desglose de base e IVA por tipo, calculado sobre el total de cada tipo.</summary>
    public IReadOnlyList<TicketVatLine> VatBreakdown() =>
        _lines.GroupBy(l => l.Item.VatRate)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var split = Vat.FromGross(g.Sum(l => l.Total), g.Key);
                return new TicketVatLine(g.Key, split.Base, split.VatAmount, split.Total);
            })
            .ToList();
}
