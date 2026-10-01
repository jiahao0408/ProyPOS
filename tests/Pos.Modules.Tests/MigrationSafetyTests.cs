using Microsoft.EntityFrameworkCore;
using Pos.Data.Migrations;

namespace Pos.Modules.Tests;

/// <summary>
/// Protege contra un fallo silencioso: una migración que reconstruye una tabla en SQLite borra sus
/// triggers. Tras aplicar TODAS las migraciones, los datos de facturación deben seguir protegidos.
/// </summary>
public sealed class MigrationSafetyTests : IDisposable
{
    private readonly TestDatabase _db = new();

    public void Dispose() => _db.Dispose();

    private HashSet<string> Triggers()
    {
        using var db = _db.Factory.CreateDbContext();
        return db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM sqlite_master WHERE type = 'trigger'").ToHashSet();
    }

    [Fact]
    public void BillingTablesAreProtected()
    {
        var triggers = Triggers();

        foreach (var table in BillingTriggers.FullyProtected.Concat(BillingTriggers.VerifactuProtected))
        {
            Assert.Contains($"TR_{table}_NoUpdate", triggers);
            Assert.Contains($"TR_{table}_NoDelete", triggers);
        }
        Assert.Contains("TR_VerifactuRecords_NoUpdate", triggers);
        Assert.Contains("TR_VerifactuRecords_NoDelete", triggers);
        Assert.Contains("TR_AuditEntries_NoUpdate", triggers);
        Assert.Contains("TR_AuditEntries_NoDelete", triggers);
    }
}
