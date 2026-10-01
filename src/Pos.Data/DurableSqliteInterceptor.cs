using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pos.Data;

/// <summary>
/// "Ninguna venta se pierde si el PC se apaga a mitad": con synchronous=FULL, cuando una venta
/// se da por guardada ya está escrita en disco, no solo en la caché del sistema.
/// </summary>
public class DurableSqliteInterceptor : DbConnectionInterceptor
{
    private const string Pragmas = "PRAGMA synchronous = FULL; PRAGMA foreign_keys = ON;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Apply(connection);
        return Task.CompletedTask;
    }

    protected virtual void Apply(DbConnection connection) => Execute(connection, Pragmas);

    protected static void Execute(DbConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}

/// <summary>
/// Abre la BD cifrada con SQLCipher usando la clave "en bruto" (x'…'): la clave ya son 32 bytes
/// aleatorios, así que no hace falta la derivación PBKDF2 que SQLCipher aplica a las contraseñas
/// (y que tarda casi un segundo en cada conexión). Tiene que ser lo primero que se ejecuta.
/// </summary>
public sealed partial class SqlCipherInterceptor : DurableSqliteInterceptor
{
    private readonly string _keyPragma;

    public SqlCipherInterceptor(string hexKey) => _keyPragma = $"PRAGMA key = \"{RawKey(hexKey)}\";";

    /// <summary>Clave en el formato de SQLCipher para claves binarias: x'…' con 64 cifras hexadecimales.</summary>
    public static string RawKey(string hexKey) =>
        HexKey().IsMatch(hexKey) ? $"x'{hexKey}'" : throw new ArgumentException("La clave debe ser de 64 cifras hexadecimales.", nameof(hexKey));

    protected override void Apply(DbConnection connection)
    {
        Execute(connection, _keyPragma);
        base.Apply(connection);
    }

    [GeneratedRegex("^[0-9A-Fa-f]{64}$")]
    private static partial Regex HexKey();
}
