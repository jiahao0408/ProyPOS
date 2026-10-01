using Pos.Core.Domain;

namespace Pos.Data;

/// <summary>Acciones auditadas (USR-03).</summary>
public static class AuditActions
{
    public const string Return = "Return";
    public const string Exchange = "Exchange";
    public const string Discount = "Discount";
    public const string PriceChange = "PriceChange";
    public const string CashDrawer = "CashDrawer";
}

/// <summary>
/// Añade una entrada al registro de auditoría en el contexto dado, para que se guarde en la
/// misma transacción que la acción (si la acción no se guarda, tampoco su rastro, y al revés).
/// </summary>
public static class AuditLog
{
    public static void Record(PosDbContext db, User? user, string action, string details, DateTime nowUtc, string? authorizedBy = null) =>
        db.AuditEntries.Add(new AuditEntry
        {
            AtUtc = nowUtc,
            UserId = user?.Id,
            UserName = user?.Name ?? "",
            AuthorizedBy = authorizedBy,
            Action = action,
            Details = details.Length > 2000 ? details[..2000] : details,
        });
}
