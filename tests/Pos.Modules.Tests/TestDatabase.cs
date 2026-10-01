using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pos.Data;

namespace Pos.Modules.Tests;

/// <summary>
/// BD SQLite en memoria con las migraciones reales aplicadas. Vive mientras la conexión
/// esté abierta, así que cada test tiene la suya y empieza vacía.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDatabase()
    {
        SQLitePCL.Batteries_V2.Init();
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<PosDbContext>().UseSqlite(_connection).Options;
        Factory = new PosDbContextFactory(options);

        using var db = Factory.CreateDbContext();
        db.Database.Migrate();
    }

    public IDbContextFactory<PosDbContext> Factory { get; }

    public void Dispose() => _connection.Dispose();
}
