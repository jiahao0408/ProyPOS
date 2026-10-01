using Microsoft.EntityFrameworkCore;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Data;

namespace Pos.Modules.CashRegister;

/// <summary>Turnos de caja. CAJ-01: no se puede vender con la caja cerrada.</summary>
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
}
