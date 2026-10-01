using Microsoft.EntityFrameworkCore;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Invoicing;
using Pos.Data;

namespace Pos.Modules.Sales;

/// <summary>
/// Cómo paga el cliente. Solo efectivo: CardAmount = 0. Solo tarjeta: CardAmount = total.
/// Mixto: una parte con tarjeta y el resto en efectivo (VEN-04).
/// </summary>
public sealed record PaymentRequest(decimal CardAmount, decimal CashTendered)
{
    public static PaymentRequest Cash(decimal tendered) => new(0m, tendered);

    public static PaymentRequest Card(decimal total) => new(total, 0m);
}

public sealed record CheckoutResult(Sale Sale, decimal Change);

/// <summary>
/// Cobro de un ticket (VEN-03, VEN-04) con descuento de stock (INV-01), todo en una transacción.
/// Los módulos registrados como <see cref="ISaleHook"/> (por ejemplo, la facturación) añaden sus datos en la misma transacción.
/// </summary>
public sealed class SalesService(IDbContextFactory<PosDbContext> dbFactory, TimeProvider clock, IEnumerable<ISaleHook> hooks)
{
    public SalesService(IDbContextFactory<PosDbContext> dbFactory, TimeProvider clock)
        : this(dbFactory, clock, [])
    {
    }

    /// <summary>Lo que falta pagar en efectivo y el cambio, sin guardar nada (para mostrarlo mientras se teclea).</summary>
    public static (decimal CashDue, decimal Change) Preview(decimal total, PaymentRequest payment)
    {
        var cashDue = Math.Max(0m, total - payment.CardAmount);
        return (cashDue, Math.Max(0m, payment.CashTendered - cashDue));
    }

    /// <param name="customer">Datos del cliente si pide factura completa (FAC-02); null = factura simplificada.</param>
    public OperationResult<CheckoutResult> Checkout(Ticket ticket, PaymentRequest payment, int userId, InvoiceCustomer? customer = null)
    {
        if (ticket.IsEmpty)
            return OperationResult<CheckoutResult>.Fail("ErrorTicketEmpty");

        var total = ticket.Total;
        if (payment.CardAmount < 0 || payment.CashTendered < 0)
            return OperationResult<CheckoutResult>.Fail("ErrorAmountNegative");
        if (HasMoreThanTwoDecimals(payment.CardAmount) || HasMoreThanTwoDecimals(payment.CashTendered))
            return OperationResult<CheckoutResult>.Fail("ErrorPriceDecimals");
        if (payment.CardAmount > total)
            return OperationResult<CheckoutResult>.Fail("ErrorCardExceedsTotal");

        var cashDue = total - payment.CardAmount;
        if (payment.CashTendered < cashDue)
            return OperationResult<CheckoutResult>.Fail("ErrorCashInsufficient"); // VEN-03: no se cobra menos del total
        var change = payment.CashTendered - cashDue;

        using var db = dbFactory.CreateDbContext();

        // CAJ-01: no se puede vender con la caja cerrada.
        var session = db.CashSessions.FirstOrDefault(s => s.ClosedAtUtc == null);
        if (session is null)
            return OperationResult<CheckoutResult>.Fail("ErrorCashClosed");

        var productIds = ticket.Lines.Select(l => l.Item.ProductId).OfType<int>().Distinct().ToList();
        var products = db.Products.Where(p => productIds.Contains(p.Id)).ToDictionary(p => p.Id);
        if (products.Count != productIds.Count)
            return OperationResult<CheckoutResult>.Fail("ErrorProductNotFound");

        var now = clock.GetUtcNow().UtcDateTime;
        var sale = new Sale
        {
            CashSessionId = session.Id,
            UserId = userId,
            CreatedAtUtc = now,
            Total = total,
            CashTendered = payment.CashTendered,
            Change = change,
            Lines = ticket.Lines.Select(l => new SaleLine
            {
                ProductId = l.Item.ProductId,
                Description = l.Item.Description,
                CategoryId = l.Item.CategoryId,
                Quantity = l.Quantity,
                UnitPrice = l.Item.UnitPrice,
                VatRate = l.Item.VatRate,
                LineTotal = l.Total,
            }).ToList(),
        };
        if (payment.CardAmount > 0)
            sale.Payments.Add(new Payment { Method = PaymentMethod.Card, Amount = payment.CardAmount });
        if (cashDue > 0)
            sale.Payments.Add(new Payment { Method = PaymentMethod.Cash, Amount = cashDue });

        var context = new CheckoutContext(sale, customer, userId, now);
        foreach (var hook in hooks)
        {
            if (hook.Validate(context) is { } error)
                return OperationResult<CheckoutResult>.Fail(error);
        }

        // Una sola transacción: venta, pagos, stock y factura, o nada.
        using var transaction = db.Database.BeginTransaction();
        db.Sales.Add(sale);

        // INV-01: vender 3 unidades resta 3.
        foreach (var line in ticket.Lines.Where(l => l.Item.ProductId is not null))
            StockLedger.Record(db, products[line.Item.ProductId!.Value], -line.Quantity, StockMovementReason.Sale, now, userId, sale);

        foreach (var hook in hooks)
            hook.OnSaleCreated(db, context);

        db.SaveChanges();
        transaction.Commit();
        return OperationResult<CheckoutResult>.Ok(new CheckoutResult(sale, change));
    }

    public Sale? GetSale(int id)
    {
        using var db = dbFactory.CreateDbContext();
        return db.Sales.AsNoTracking()
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .FirstOrDefault(s => s.Id == id);
    }

    private static bool HasMoreThanTwoDecimals(decimal amount) => decimal.Round(amount, 2) != amount;
}
