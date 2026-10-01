using System.Diagnostics;
using System.Text;
using Microsoft.Data.Sqlite;
using Pos.Data;

namespace Pos.Modules.Tests;

/// <summary>BD real en disco: SQLCipher + clave protegida (DPAPI en Windows).</summary>
public sealed class EncryptedDatabaseTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("starseapos-db-").FullName;

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // Windows puede tardar en soltar el fichero; es una carpeta temporal
        }
    }

    [Fact]
    public void Open_CreatesEncryptedDatabaseQuickly()
    {
        var watch = Stopwatch.StartNew();
        var factory = PosDatabase.Open(_directory);
        watch.Stop();

        using (var db = factory.CreateDbContext())
            Assert.Empty(db.Users);
        SqliteConnection.ClearAllPools();

        var header = Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(_directory, "pos.db")).Take(15).ToArray());
        Assert.NotEqual("SQLite format 3", header); // cifrada: no se ve la cabecera de SQLite
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"Abrir y migrar tardó {watch.Elapsed}");
    }

    [Fact]
    public void Open_TwiceReusesTheSameKey()
    {
        var first = PosDatabase.Open(_directory);
        using (var db = first.CreateDbContext())
        {
            db.Settings.Add(new Core.Domain.Setting { Key = "k", Value = "v" });
            db.SaveChanges();
        }
        SqliteConnection.ClearAllPools();

        var second = PosDatabase.Open(_directory);
        using var again = second.CreateDbContext();

        Assert.Equal("v", again.Settings.Single(s => s.Key == "k").Value);
    }

    [Fact]
    public void WrongKey_CannotReadTheDatabase()
    {
        var factory = PosDatabase.Open(_directory);
        using (var db = factory.CreateDbContext())
            _ = db.Users.Count();
        SqliteConnection.ClearAllPools();

        using var wrong = new Data.PosDbContext(PosDatabase.CreateOptions(Path.Combine(_directory, "pos.db"), "clave-incorrecta"));

        Assert.ThrowsAny<SqliteException>(() => wrong.Users.Count());
    }
}
