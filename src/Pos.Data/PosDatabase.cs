using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Pos.Data;

public static class PosDatabase
{
    /// <summary>Carpeta de datos de la app: %LOCALAPPDATA%\StarSeaPOS en Windows.</summary>
    public static string DefaultDataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StarSeaPOS");

    /// <summary>Opciones para abrir la BD local cifrada con SQLCipher.</summary>
    public static DbContextOptions<PosDbContext> CreateOptions(string databasePath, string encryptionKey)
    {
        if (string.IsNullOrEmpty(encryptionKey))
            throw new ArgumentException("La base de datos debe abrirse siempre con clave.", nameof(encryptionKey));

        SQLitePCL.Batteries_V2.Init();

        // Sin pool: la clave se pone en cada apertura (con clave en bruto cuesta muy poco), así nunca
        // se reutiliza una conexión que no haya pasado por el interceptor.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();

        return new DbContextOptionsBuilder<PosDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new SqlCipherInterceptor(encryptionKey))
            .Options;
    }

    /// <summary>Abre (o crea) la BD de la carpeta indicada y aplica las migraciones pendientes.</summary>
    public static IDbContextFactory<PosDbContext> Open(string dataDirectory) => OpenFile(dataDirectory).Factory;

    /// <summary>Como <see cref="Open"/>, pero devuelve también la ruta y la clave (para copias de seguridad).</summary>
    public static DatabaseFile OpenFile(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        var key = DatabaseKeyStore.GetOrCreateKey(Path.Combine(dataDirectory, "pos.key"));
        var path = Path.Combine(dataDirectory, "pos.db");
        var factory = new PosDbContextFactory(CreateOptions(path, key));

        using var db = factory.CreateDbContext();
        db.Database.Migrate();

        return new DatabaseFile(factory, path, key);
    }
}

/// <summary>BD abierta: el factory para usarla, y su fichero y clave para copiarla o restaurarla (DAT-03).</summary>
public sealed record DatabaseFile(IDbContextFactory<PosDbContext> Factory, string Path, string Key)
{
    public string DataDirectory => System.IO.Path.GetDirectoryName(Path)!;

    public string BackupsDirectory => System.IO.Path.Combine(DataDirectory, "backups");
}

public sealed class PosDbContextFactory(DbContextOptions<PosDbContext> options) : IDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext() => new(options);
}

/// <summary>Solo para "dotnet ef migrations add": las migraciones no necesitan la clave real.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<PosDbContext>
{
    public PosDbContext CreateDbContext(string[] args) =>
        new(PosDatabase.CreateOptions("pos-design.db", new string('0', 64)));
}
