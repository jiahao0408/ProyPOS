using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pos.Data;

public static class PosDatabase
{
    /// <summary>Carpeta de datos de la app: %LOCALAPPDATA%\ProyPOS en Windows.</summary>
    public static string DefaultDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ProyPOS");

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

    /// <summary>Abre (o crea) la BD de la carpeta indicada y aplica las migraciones pendientes.</summary>
    public static IDbContextFactory<PosDbContext> Open(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var key = DatabaseKeyStore.GetOrCreateKey(Path.Combine(dataDirectory, "pos.key"));
        var options = CreateOptions(Path.Combine(dataDirectory, "pos.db"), key);
        var factory = new PosDbContextFactory(options);

        using var db = factory.CreateDbContext();
        db.Database.Migrate();

        return factory;
    }
}

public sealed class PosDbContextFactory(DbContextOptions<PosDbContext> options) : IDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext() => new(options);
}

/// <summary>Solo para "dotnet ef migrations add": las migraciones no necesitan la clave real.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext(string[] args) =>
        new(PosDatabase.CreateOptions("pos-design.db", "design-time-only"));
}
