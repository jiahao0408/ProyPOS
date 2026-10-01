using Microsoft.EntityFrameworkCore;
using Pos.Core.Domain;
using Pos.Data;

namespace Pos.Modules.CashRegister;

/// <param name="Count">Unidades (por producto) o tickets (por cajero o método de pago).</param>
public sealed record ReportLine(string Name, int Count, decimal Total);

/// <summary>Ventas de un periodo. Las devoluciones restan: los totales son lo realmente cobrado.</summary>
public sealed record SalesReport(
    DateOnly From,
    DateOnly To,
    int TicketCount,
    decimal SalesTotal,
    int ReturnCount,
    decimal ReturnsTotal,
    IReadOnlyList<ReportLine> ByProduct,
    IReadOnlyList<ReportLine> ByPayment,
    IReadOnlyList<ReportLine> ByCashier)
{
    /// <summary>Ventas menos devoluciones.</summary>
    public decimal NetTotal => SalesTotal + ReturnsTotal;

    public decimal AverageTicket => TicketCount == 0 ? 0 : Math.Round(SalesTotal / TicketCount, 2, MidpointRounding.AwayFromZero);
}

/// <summary>CAJ-03: ventas por producto, método de pago y cajero, entre dos fechas (ambas incluidas, en hora local).</summary>
public sealed class SalesReportService(IDbContextFactory<PosDbContext> dbFactory)
{
    /// <summary>Nombre con el que sale el método de pago; la interfaz lo traduce.</summary>
    public static string PaymentKey(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "PayCash",
        PaymentMethod.Card => "PayCard",
        _ => "PayStoreCredit",
    };

    public SalesReport Build(DateOnly from, DateOnly to)
    {
        if (to < from)
            (from, to) = (to, from);
        var fromUtc = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var toUtc = to.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();

        using var db = dbFactory.CreateDbContext();
        var sales = db.Sales.AsNoTracking()
            .Where(s => s.CreatedAtUtc >= fromUtc && s.CreatedAtUtc < toUtc)
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .ToList();
        var users = db.Users.AsNoTracking().ToDictionary(u => u.Id, u => u.Name);

        var normal = sales.Where(s => s.Kind == SaleKind.Sale).ToList();
        var returns = sales.Where(s => s.Kind == SaleKind.Return).ToList();

        var byProduct = sales.SelectMany(s => s.Lines)
            .GroupBy(l => l.Description)
            .Select(g => new ReportLine(g.Key, g.Sum(l => l.Quantity), g.Sum(l => l.LineTotal)))
            .OrderByDescending(l => l.Total).ThenBy(l => l.Name)
            .ToList();

        // El vale de un cambio no es dinero: se compensa entre la devolución y la venta nueva.
        var byPayment = sales.SelectMany(s => s.Payments.Select(p => (Sale: s, Payment: p)))
            .Where(x => x.Payment.Method != PaymentMethod.StoreCredit)
            .GroupBy(x => x.Payment.Method)
            .OrderBy(g => g.Key)
            .Select(g => new ReportLine(PaymentKey(g.Key), g.Select(x => x.Sale.Id).Distinct().Count(), g.Sum(x => x.Payment.Amount)))
            .ToList();

        var byCashier = sales
            .GroupBy(s => s.UserId)
            .Select(g => new ReportLine(users.GetValueOrDefault(g.Key, "?"), g.Count(s => s.Kind == SaleKind.Sale), g.Sum(s => s.Total)))
            .OrderByDescending(l => l.Total).ThenBy(l => l.Name)
            .ToList();

        return new SalesReport(from, to, normal.Count, normal.Sum(s => s.Total), returns.Count, returns.Sum(s => s.Total),
            byProduct, byPayment, byCashier);
    }
}
