using Microsoft.EntityFrameworkCore;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Pricing;
using Pos.Data;

namespace Pos.Modules.CashRegister;

/// <summary>Totales del turno para el arqueo y el cierre Z (CAJ-02).</summary>
public sealed record CashSummary(
    CashSession Session,
    int SalesCount,
    decimal SalesTotal,
    decimal CashTotal,
    decimal CardTotal,
    decimal ExpectedCash,
    IReadOnlyList<VatBreakdownLine> VatLines);

public sealed record VatBreakdownLine(decimal Rate, decimal Base, decimal VatAmount, decimal Total);

/// <summary>Turnos de caja. CAJ-01: no se puede vender con la caja cerrada. CAJ-02: cierre con arqueo y Z.</summary>
public sealed class CashRegisterService(IDbContextFactory<PosDbContext> dbFactory, TimeProvider clock)
{
    public CashSession? GetOpenSession()
    {
        using var db = dbFactory.CreateDbContext();
        return db.CashSessions.AsNoTracking().FirstOrDefault(s => s.ClosedAtUtc == null);
    }

    public bool IsOpen => GetOpenSession() is not null;

    public OperationResult<CashSession> Open(int userId, decimal openingFloat)
    {
        if (openingFloat < 0)
            return OperationResult<CashSession>.Fail("ErrorFloatNegative");
        if (decimal.Round(openingFloat, 2) != openingFloat)
            return OperationResult<CashSession>.Fail("ErrorPriceDecimals");

        using var db = dbFactory.CreateDbContext();
        if (db.CashSessions.Any(s => s.ClosedAtUtc == null))
            return OperationResult<CashSession>.Fail("ErrorCashAlreadyOpen");

        var session = new CashSession
        {
            OpenedByUserId = userId,
            OpenedAtUtc = clock.GetUtcNow().UtcDateTime,
            OpeningFloat = openingFloat,
        };
        db.CashSessions.Add(session);
        db.SaveChanges();
        return OperationResult<CashSession>.Ok(session);
    }

    /// <summary>Lo que debería haber en el cajón: fondo inicial + cobros en efectivo del turno.</summary>
    public CashSummary? GetSummary(int sessionId)
    {
        using var db = dbFactory.CreateDbContext();
        var session = db.CashSessions.AsNoTracking().FirstOrDefault(s => s.Id == sessionId);
        return session is null ? null : Summarize(db, session);
    }

    /// <summary>
    /// CAJ-02: el cajero cuenta el efectivo; se calcula el descuadre y se cierra el turno
    /// con un número Z correlativo. Después no se puede vender hasta abrir otro turno.
    /// </summary>
    public OperationResult<CashSummary> Close(int userId, decimal countedCash)
    {
        if (countedCash < 0)
            return OperationResult<CashSummary>.Fail("ErrorAmountNegative");
        if (decimal.Round(countedCash, 2) != countedCash)
            return OperationResult<CashSummary>.Fail("ErrorPriceDecimals");

        using var db = dbFactory.CreateDbContext();
        var session = db.CashSessions.FirstOrDefault(s => s.ClosedAtUtc == null);
        if (session is null)
            return OperationResult<CashSummary>.Fail("ErrorCashClosed");

        var summary = Summarize(db, session);
        session.ClosedAtUtc = clock.GetUtcNow().UtcDateTime;
        session.ClosedByUserId = userId;
        session.CountedCash = countedCash;
        session.ExpectedCash = summary.ExpectedCash;
        session.SalesCount = summary.SalesCount;
        session.SalesTotal = summary.SalesTotal;
        session.CashTotal = summary.CashTotal;
        session.CardTotal = summary.CardTotal;
        session.ZNumber = (db.CashSessions.Max(s => s.ZNumber) ?? 0) + 1;
        db.SaveChanges();

        return OperationResult<CashSummary>.Ok(summary with { Session = session });
    }

    public IReadOnlyList<CashSession> GetClosedSessions(int limit = 30)
    {
        using var db = dbFactory.CreateDbContext();
        return db.CashSessions.AsNoTracking().Where(s => s.ClosedAtUtc != null).OrderByDescending(s => s.Id).Take(limit).ToList();
    }

    private static CashSummary Summarize(PosDbContext db, CashSession session)
    {
        var sales = db.Sales.AsNoTracking()
            .Where(s => s.CashSessionId == session.Id)
            .Include(s => s.Payments)
            .Include(s => s.Lines)
            .ToList();

        var payments = sales.SelectMany(s => s.Payments).ToList();
        var cash = payments.Where(p => p.Method == PaymentMethod.Cash).Sum(p => p.Amount);
        var card = payments.Where(p => p.Method == PaymentMethod.Card).Sum(p => p.Amount);
        var vat = sales.SelectMany(s => s.Lines)
            .GroupBy(l => l.VatRate)
            .OrderByDescending(g => g.Key)
            .Select(g =>
            {
                var split = Vat.FromGross(g.Sum(l => l.LineTotal), g.Key);
                return new VatBreakdownLine(g.Key, split.Base, split.VatAmount, split.Total);
            })
            .ToList();

        return new CashSummary(session, sales.Count, sales.Sum(s => s.Total), cash, card, session.OpeningFloat + cash, vat);
    }
}
