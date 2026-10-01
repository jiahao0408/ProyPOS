using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pos.Data;

public static class PosDatabase
{
    /// <summary>Opciones para abrir la BD local cifrada con SQLCipher.</summary>
    public static DbContextOptions<PosDbContext> CreateOptions(string databasePath, string encryptionKey)
    {
        if (string.IsNullOrEmpty(encryptionKey))
            throw new ArgumentException("La base de datos debe abrirse siempre con clave.", nameof(encryptionKey));

        SQLitePCL.Batteries_V2.Init();

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Password = encryptionKey,
        }.ToString();

        return new DbContextOptionsBuilder<PosDbContext>()
            .UseSqlite(connectionString)
            .Options;
    }

    /// <summary>Crea la BD si no existe y aplica las migraciones pendientes.</summary>
    public static async Task MigrateAsync(PosDbContext db, CancellationToken ct = default) =>
        await db.Database.MigrateAsync(ct);
}

/// <summary>Solo para "dotnet ef migrations add": las migraciones no necesitan la clave real.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext(string[] args) =>
        new(PosDatabase.CreateOptions("pos-design.db", "design-time-only"));
}
