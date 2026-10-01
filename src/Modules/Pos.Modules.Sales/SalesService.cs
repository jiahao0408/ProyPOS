using System.Globalization;
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

/// <summary>Unidades a devolver de una línea de la venta original.</summary>
public sealed record ReturnLineRequest(int OriginalLineId, int Quantity);

/// <summary>Línea de una venta con las unidades que aún se pueden devolver.</summary>
public sealed record ReturnableLine(SaleLine Line, int Returnable)
{
    /// <summary>Lo que se devuelve por unidad: el importe pagado (con descuentos) entre las unidades.</summary>
    public decimal UnitRefund => Line.Quantity == 0 ? 0 : Line.LineTotal / Line.Quantity;
}

/// <summary>BAZ-07: un cambio son dos documentos en una operación, la devolución y la venta nueva.</summary>
public sealed record ExchangeResult(Sale Return, Sale NewSale, decimal Difference, decimal Change);

public static class SalesSettingKeys
{
    /// <summary>VEN-05: descuento máximo (en %) que puede aplicar un cajero sin PIN de admin.</summary>
    public const string MaxCashierDiscount = "sales.maxCashierDiscount";

    public const decimal DefaultMaxCashierDiscount = 10m;
}

/// <summary>
/// Cobro de un ticket (VEN-03, VEN-04) con devoluciones (VEN-06) y cambios (BAZ-07),
/// cada uno en una sola transacción. Los módulos registrados como <see cref="ISaleHook"/> (la facturación,
/// que a su vez avisa a Verifactu) añaden sus datos en esa misma transacción.
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

    /// <summary>VEN-05: límite de descuento del cajero, que fija el admin.</summary>
    public decimal MaxCashierDiscount
    {
        get
        {
            using var db = dbFactory.CreateDbContext();
            var value = db.Settings.AsNoTracking().Where(s => s.Key == SalesSettingKeys.MaxCashierDiscount).Select(s => s.Value).FirstOrDefault();
            return decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var max) ? max : SalesSettingKeys.DefaultMaxCashierDiscount;
        }
    }

    /// <param name="customer">Datos del cliente si pide factura completa (FAC-02); null = factura simplificada.</param>
    /// <param name="discountAuthorizedBy">Admin que autorizó con su PIN un descuento por encima del límite del cajero.</param>
    public OperationResult<CheckoutResult> Checkout(Ticket ticket, PaymentRequest payment, int userId,
        InvoiceCustomer? customer = null, string? discountAuthorizedBy = null)
    {
        if (ticket.IsEmpty)
            return OperationResult<CheckoutResult>.Fail("ErrorTicketEmpty");
        if (ValidatePayment(ticket.Total, payment) is { } paymentError)
            return OperationResult<CheckoutResult>.Fail(paymentError);

        using var db = dbFactory.CreateDbContext();
        var user = db.Users.Find(userId);
        if (ticket.Discount > 0 && user?.IsAdmin != true && discountAuthorizedBy is null
            && ticket.MaxEffectiveDiscountPercent > MaxCashierDiscount)
            return OperationResult<CheckoutResult>.Fail("ErrorDiscountLimit");

        var session = db.CashSessions.FirstOrDefault(s => s.ClosedAtUtc == null);
        if (session is null)
            return OperationResult<CheckoutResult>.Fail("ErrorCashClosed"); // CAJ-01

        var now = clock.GetUtcNow().UtcDateTime;
        if (!ProductsExist(db, ticket))
            return OperationResult<CheckoutResult>.Fail("ErrorProductNotFound");

        var (sale, change) = BuildSale(ticket, payment, session.Id, userId, now);
        var context = new CheckoutContext(sale, customer, userId, now);
        if (Validate(context) is { } hookError)
            return OperationResult<CheckoutResult>.Fail(hookError);

        using var transaction = db.Database.BeginTransaction();
        Save(db, sale, context);
        if (ticket.Discount > 0)
        {
            AuditLog.Record(db, user, AuditActions.Discount,
                $"{ticket.Discount.ToString("0.00", CultureInfo.InvariantCulture)} € de descuento sobre " +
                $"{(ticket.Total + ticket.Discount).ToString("0.00", CultureInfo.InvariantCulture)} € " +
                $"(máx. {ticket.MaxEffectiveDiscountPercent.ToString("0.##", CultureInfo.InvariantCulture)} %)",
                now, discountAuthorizedBy);
        }
        db.SaveChanges();
        transaction.Commit();
        return OperationResult<CheckoutResult>.Ok(new CheckoutResult(sale, change));
    }

    /// <summary>Unidades de cada línea que aún se pueden devolver (lo vendido menos lo ya devuelto).</summary>
    public IReadOnlyList<ReturnableLine> GetReturnable(int saleId)
    {
        using var db = dbFactory.CreateDbContext();
        return Returnable(db, saleId);
    }

    /// <summary>
    /// VEN-06: devolución de una venta cerrada. Motivo obligatorio (el PIN de admin lo pide la interfaz
    /// y llega en <paramref name="authorizedBy"/>). Reembolsa con el método indicado y
    /// genera la factura rectificativa (FAC-03). La venta original no se toca.
    /// </summary>
    public OperationResult<Sale> Return(int originalSaleId, IReadOnlyList<ReturnLineRequest> lines, string reason,
        PaymentMethod refundMethod, int userId, string? authorizedBy = null)
    {
        if (refundMethod == PaymentMethod.StoreCredit)
            return OperationResult<Sale>.Fail("ErrorRefundMethod");

        using var db = dbFactory.CreateDbContext();
        var session = db.CashSessions.FirstOrDefault(s => s.ClosedAtUtc == null);
        if (session is null)
            return OperationResult<Sale>.Fail("ErrorCashClosed");

        var now = clock.GetUtcNow().UtcDateTime;
        var built = BuildReturn(db, originalSaleId, lines, reason, session.Id, userId, now);
        if (!built.Success)
            return OperationResult<Sale>.Fail(built.ErrorKey!);

        var returnSale = built.Value!;
        returnSale.Payments.Add(new Payment { Method = refundMethod, Amount = returnSale.Total });
        var context = new CheckoutContext(returnSale, null, userId, now);
        if (Validate(context) is { } hookError)
            return OperationResult<Sale>.Fail(hookError);

        using var transaction = db.Database.BeginTransaction();
        Save(db, returnSale, context);
        AuditLog.Record(db, db.Users.Find(userId), AuditActions.Return,
            $"Venta {originalSaleId} · {Describe(returnSale)} · {(-returnSale.Total).ToString("0.00", CultureInfo.InvariantCulture)} € ({refundMethod}) · motivo: {reason.Trim()}",
            now, authorizedBy);
        db.SaveChanges();
        transaction.Commit();
        return OperationResult<Sale>.Ok(returnSale);
    }

    /// <summary>
    /// BAZ-07: cambio de producto. En una operación se devuelve lo indicado (con su rectificativa) y se vende el
    /// ticket nuevo. Lo devuelto paga lo nuevo como vale interno; el cliente paga la diferencia con
    /// <paramref name="payment"/> o se le devuelve con <paramref name="refundMethod"/>.
    /// </summary>
    public OperationResult<ExchangeResult> Exchange(int originalSaleId, IReadOnlyList<ReturnLineRequest> lines, Ticket newTicket,
        PaymentRequest payment, PaymentMethod refundMethod, int userId, string reason = "Cambio", string? authorizedBy = null)
    {
        if (newTicket.IsEmpty)
            return OperationResult<ExchangeResult>.Fail("ErrorTicketEmpty");
        if (refundMethod == PaymentMethod.StoreCredit)
            return OperationResult<ExchangeResult>.Fail("ErrorRefundMethod");

        using var db = dbFactory.CreateDbContext();
        var session = db.CashSessions.FirstOrDefault(s => s.ClosedAtUtc == null);
        if (session is null)
            return OperationResult<ExchangeResult>.Fail("ErrorCashClosed");

        var now = clock.GetUtcNow().UtcDateTime;
        var built = BuildReturn(db, originalSaleId, lines, reason, session.Id, userId, now);
        if (!built.Success)
            return OperationResult<ExchangeResult>.Fail(built.ErrorKey!);
        var returnSale = built.Value!;
        if (!ProductsExist(db, newTicket))
            return OperationResult<ExchangeResult>.Fail("ErrorProductNotFound");

        var credit = -returnSale.Total;           // lo que vale lo devuelto
        var difference = newTicket.Total - credit; // > 0: paga el cliente; < 0: se le devuelve
        var usedCredit = Math.Min(credit, newTicket.Total);

        // Devolución: el vale cubre lo nuevo; si sobra, se reembolsa.
        returnSale.Payments.Add(new Payment { Method = PaymentMethod.StoreCredit, Amount = -usedCredit });
        if (difference < 0)
            returnSale.Payments.Add(new Payment { Method = refundMethod, Amount = difference });

        // Venta nueva: se paga con el vale y, si falta, con lo que entregue el cliente.
        Sale newSale;
        var change = 0m;
        if (difference > 0)
        {
            if (ValidatePayment(difference, payment) is { } paymentError)
                return OperationResult<ExchangeResult>.Fail(paymentError);
            (newSale, change) = BuildSale(newTicket, payment, session.Id, userId, now, amountDue: difference);
        }
        else
        {
            (newSale, change) = BuildSale(newTicket, PaymentRequest.Cash(0), session.Id, userId, now, amountDue: 0);
        }
        newSale.Payments.Insert(0, new Payment { Method = PaymentMethod.StoreCredit, Amount = usedCredit });

        var returnContext = new CheckoutContext(returnSale, null, userId, now);
        var saleContext = new CheckoutContext(newSale, null, userId, now);
        if ((Validate(returnContext) ?? Validate(saleContext)) is { } hookError)
            return OperationResult<ExchangeResult>.Fail(hookError);

        using var transaction = db.Database.BeginTransaction();
        Save(db, returnSale, returnContext);
        Save(db, newSale, saleContext);
        AuditLog.Record(db, db.Users.Find(userId), AuditActions.Exchange,
            $"Venta {originalSaleId} · devuelve {Describe(returnSale)} · se lleva {Describe(newSale)} · diferencia {difference.ToString("0.00", CultureInfo.InvariantCulture)} €",
            now, authorizedBy);
        db.SaveChanges();
        transaction.Commit();
        return OperationResult<ExchangeResult>.Ok(new ExchangeResult(returnSale, newSale, difference, change));
    }

    public Sale? GetSale(int id)
    {
        using var db = dbFactory.CreateDbContext();
        return db.Sales.AsNoTracking()
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .FirstOrDefault(s => s.Id == id);
    }

    // --- Pasos comunes ---

    private static string? ValidatePayment(decimal due, PaymentRequest payment)
    {
        if (payment.CardAmount < 0 || payment.CashTendered < 0)
            return "ErrorAmountNegative";
        if (HasMoreThanTwoDecimals(payment.CardAmount) || HasMoreThanTwoDecimals(payment.CashTendered))
            return "ErrorPriceDecimals";
        if (payment.CardAmount > due)
            return "ErrorCardExceedsTotal";
        return payment.CashTendered < due - payment.CardAmount ? "ErrorCashInsufficient" : null; // VEN-03
    }

    /// <summary>Venta a partir del ticket. <paramref name="amountDue"/>: lo que se cobra con dinero (en un cambio, solo la diferencia).</summary>
    private static (Sale Sale, decimal Change) BuildSale(Ticket ticket, PaymentRequest payment, int sessionId, int userId, DateTime now, decimal? amountDue = null)
    {
        var due = amountDue ?? ticket.Total;
        var cashDue = due - payment.CardAmount;
        var change = due == 0 ? 0 : payment.CashTendered - cashDue;
        var sale = new Sale
        {
            CashSessionId = sessionId,
            UserId = userId,
            CreatedAtUtc = now,
            Total = ticket.Total,
            CashTendered = due == 0 ? 0 : payment.CashTendered,
            Change = change,
            DiscountPercent = ticket.DiscountPercent,
            Lines = ticket.Lines.Select(l => new SaleLine
            {
                ProductId = l.Item.ProductId,
                Description = l.Item.Description,
                CategoryId = l.Item.CategoryId,
                Quantity = l.Quantity,
                UnitPrice = l.Item.UnitPrice,
                VatRate = l.Item.VatRate,
                LineTotal = l.Total,
                Discount = l.Discount,
            }).ToList(),
        };
        if (payment.CardAmount > 0)
            sale.Payments.Add(new Payment { Method = PaymentMethod.Card, Amount = payment.CardAmount });
        if (cashDue > 0)
            sale.Payments.Add(new Payment { Method = PaymentMethod.Cash, Amount = cashDue });
        return (sale, change);
    }

    private static OperationResult<Sale> BuildReturn(PosDbContext db, int originalSaleId,
        IReadOnlyList<ReturnLineRequest> requests, string reason, int sessionId, int userId, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return OperationResult<Sale>.Fail("ErrorReasonRequired");
        var wanted = requests.Where(r => r.Quantity > 0).ToList();
        if (wanted.Count == 0)
            return OperationResult<Sale>.Fail("ErrorNothingToReturn");

        var original = db.Sales.AsNoTracking().FirstOrDefault(s => s.Id == originalSaleId);
        if (original is null || original.Kind != SaleKind.Sale)
            return OperationResult<Sale>.Fail("ErrorSaleNotFound");

        var returnable = Returnable(db, originalSaleId).ToDictionary(r => r.Line.Id);
        var sale = new Sale
        {
            Kind = SaleKind.Return,
            OriginalSaleId = originalSaleId,
            Reason = reason.Trim(),
            CashSessionId = sessionId,
            UserId = userId,
            CreatedAtUtc = now,
        };
        foreach (var request in wanted)
        {
            if (!returnable.TryGetValue(request.OriginalLineId, out var line) || request.Quantity > line.Returnable)
                return OperationResult<Sale>.Fail("ErrorReturnTooMany");

            // Lo pagado por esas unidades (con su parte de descuento). La última unidad devuelve el resto
            // exacto, para que la suma de devoluciones nunca pase de lo cobrado.
            var refund = request.Quantity == line.Returnable
                ? line.Line.LineTotal - AlreadyRefunded(db, line.Line.Id)
                : Math.Round(line.UnitRefund * request.Quantity, 2, MidpointRounding.AwayFromZero);
            sale.Lines.Add(new SaleLine
            {
                OriginalLineId = line.Line.Id,
                ProductId = line.Line.ProductId,
                Description = line.Line.Description,
                CategoryId = line.Line.CategoryId,
                Quantity = -request.Quantity,
                UnitPrice = line.Line.UnitPrice,
                VatRate = line.Line.VatRate,
                LineTotal = -refund,
            });
        }
        sale.Total = sale.Lines.Sum(l => l.LineTotal);

        return OperationResult<Sale>.Ok(sale);
    }

    private static List<ReturnableLine> Returnable(PosDbContext db, int saleId)
    {
        var lines = db.SaleLines.AsNoTracking().Where(l => l.SaleId == saleId).OrderBy(l => l.Id).ToList();
        var ids = lines.Select(l => l.Id).ToList();
        var returned = db.SaleLines.AsNoTracking()
            .Where(l => l.OriginalLineId != null && ids.Contains(l.OriginalLineId.Value))
            .GroupBy(l => l.OriginalLineId!.Value)
            .Select(g => new { Id = g.Key, Quantity = -g.Sum(l => l.Quantity) })
            .ToDictionary(x => x.Id, x => x.Quantity);
        return lines.Select(l => new ReturnableLine(l, l.Quantity - returned.GetValueOrDefault(l.Id))).ToList();
    }

    private static decimal AlreadyRefunded(PosDbContext db, int lineId) =>
        -db.SaleLines.AsNoTracking().Where(l => l.OriginalLineId == lineId).Select(l => l.LineTotal).AsEnumerable().Sum();

    private static bool ProductsExist(PosDbContext db, Ticket ticket)
    {
        var ids = ticket.Lines.Select(l => l.Item.ProductId).OfType<int>().Distinct().ToList();
        return db.Products.Count(p => ids.Contains(p.Id)) == ids.Count;
    }

    private string? Validate(CheckoutContext context) =>
        hooks.Select(h => h.Validate(context)).FirstOrDefault(e => e is not null);

    /// <summary>Guarda la venta (o devolución) y avisa a los hooks (factura, Verifactu). La tienda no lleva stock.</summary>
    private void Save(PosDbContext db, Sale sale, CheckoutContext context)
    {
        db.Sales.Add(sale);
        foreach (var hook in hooks)
            hook.OnSaleCreated(db, context);
    }

    private static string Describe(Sale sale) =>
        string.Join(", ", sale.Lines.Select(l => $"{Math.Abs(l.Quantity)} x {l.Description}"));

    private static bool HasMoreThanTwoDecimals(decimal amount) => decimal.Round(amount, 2) != amount;
}
