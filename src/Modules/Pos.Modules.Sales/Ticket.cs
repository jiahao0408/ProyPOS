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

public sealed class TicketLine(Ticket owner, TicketItem item, int quantity)
{
    public TicketItem Item { get; } = item;

    public int Quantity { get; internal set; } = quantity;

    /// <summary>Descuento de la línea en % (VEN-05); null si no hay o si es por importe.</summary>
    public decimal? DiscountPercent { get; internal set; }

    /// <summary>Descuento de la línea por importe (VEN-05); null si no hay o si es en %.</summary>
    public decimal? DiscountAmount { get; internal set; }

    /// <summary>Precio × cantidad, sin descuentos.</summary>
    public decimal Gross => Math.Round(Item.UnitPrice * Quantity, 2, MidpointRounding.AwayFromZero);

    /// <summary>Importe final de la línea: descuento de la línea y, después, el del total del ticket.</summary>
    public decimal Total
    {
        get
        {
            var lineDiscount = DiscountPercent is { } p ? Round(Gross * p / 100m)
                : DiscountAmount is { } a ? Math.Min(a, Gross)
                : 0m;
            return Round((Gross - lineDiscount) * (1 - owner.DiscountPercent / 100m));
        }
    }

    /// <summary>Todo lo descontado en esta línea (también su parte del descuento del total).</summary>
    public decimal Discount => Gross - Total;

    /// <summary>Descuento efectivo en %, para comprobar el límite del cajero.</summary>
    public decimal EffectiveDiscountPercent => Gross == 0 ? 0 : Math.Round(Discount / Gross * 100m, 2);

    private static decimal Round(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
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

        var line = new TicketLine(this, item, quantity);
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

    public void Clear()
    {
        _lines.Clear();
        DiscountPercent = 0;
    }

    /// <summary>VEN-05: descuento sobre el total del ticket, en %. Se reparte en todas las líneas.</summary>
    public decimal DiscountPercent { get; private set; }

    public decimal Discount => _lines.Sum(l => l.Discount);

    /// <summary>El mayor descuento efectivo de una línea: lo que se compara con el límite del cajero.</summary>
    public decimal MaxEffectiveDiscountPercent => _lines.Count == 0 ? 0 : _lines.Max(l => l.EffectiveDiscountPercent);

    public void SetDiscountPercent(decimal percent) => DiscountPercent = ValidPercent(percent);

    /// <summary>Descuento de una línea, en % o por importe (uno de los dos; ambos null lo quita).</summary>
    public void SetLineDiscount(TicketLine line, decimal? percent, decimal? amount)
    {
        if (percent is not null && amount is not null)
            throw new ArgumentException("Solo un tipo de descuento por línea.");
        if (amount is < 0)
            throw new ArgumentOutOfRangeException(nameof(amount));
        line.DiscountPercent = percent is { } p ? ValidPercent(p) : null;
        line.DiscountAmount = amount;
    }

    private static decimal ValidPercent(decimal percent) =>
        percent is < 0 or > 100 ? throw new ArgumentOutOfRangeException(nameof(percent)) : percent;

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
