using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Pos.Data;

/// <summary>
/// "Ninguna venta se pierde si el PC se apaga a mitad": con synchronous=FULL, cuando una venta
/// se da por guardada ya está escrita en disco, no solo en la caché del sistema.
/// </summary>
public sealed class DurableSqliteInterceptor : DbConnectionInterceptor
{
    private const string Pragmas = "PRAGMA synchronous = FULL; PRAGMA foreign_keys = ON;";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData) =>
        Apply(connection);

    public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Apply(connection);
        return Task.CompletedTask;
    }

    private static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = Pragmas;
        command.ExecuteNonQuery();
    }
}
