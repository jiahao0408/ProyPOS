using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Pos.Core;
using Pos.Data;

namespace Pos.Modules.DataTransfer;

/// <summary>
/// DAT-03: copia completa de la BD y restauración.
///
/// La BD del PC está cifrada con una clave que solo sirve en ese PC (DPAPI). Una copia así no se podría
/// restaurar en un PC nuevo si el viejo se rompe, así que la copia se cifra con una contraseña que elige
/// el admin: AES-256-GCM con clave derivada de la contraseña (PBKDF2-SHA256, 600.000 iteraciones).
/// Formato: "SSPOSBK1" | sal (16) | nonce (12) | etiqueta (16) | BD comprimida y cifrada.
///
/// Además hay una copia automática diaria (requisito de fiabilidad) en la carpeta backups, cifrada con
/// la clave del PC; se guardan las 7 últimas.
/// </summary>
public sealed class BackupService(DatabaseFile? database, TimeProvider clock)
{
    public const int MinPasswordLength = 8;
    public const int AutoBackupsKept = 7;
    private const int Iterations = 600_000;
    private static readonly byte[] Magic = "SSPOSBK1"u8.ToArray();

    /// <summary>En los tests de interfaz la BD está en memoria y no hay fichero que copiar.</summary>
    public bool IsAvailable => database is not null;

    public OperationResult CreateBackup(string destinationPath, string password)
    {
        if (database is null)
            return OperationResult.Fail("ErrorBackupUnavailable");
        if (password.Length < MinPasswordLength)
            return OperationResult.Fail("ErrorBackupPasswordShort");

        var plain = TempFile();
        try
        {
            ExportPlain(database, plain);
            var compressed = Compress(File.ReadAllBytes(plain));
            File.WriteAllBytes(destinationPath, Encrypt(compressed, password));
            return OperationResult.Ok();
        }
        finally
        {
            DeleteQuietly(plain);
        }
    }

    /// <summary>
    /// Restaura una copia. Antes guarda una copia del estado actual en la carpeta backups
    /// ("restaurar guarda antes una copia del estado actual"). La confirmación la pide la interfaz.
    /// </summary>
    public OperationResult Restore(string backupPath, string password)
    {
        if (database is null)
            return OperationResult.Fail("ErrorBackupUnavailable");

        byte[] compressed;
        try
        {
            compressed = Decrypt(File.ReadAllBytes(backupPath), password);
        }
        catch (InvalidDataException)
        {
            return OperationResult.Fail("ErrorBackupInvalid");
        }
        catch (AuthenticationTagMismatchException)
        {
            return OperationResult.Fail("ErrorBackupPassword");
        }

        var plain = TempFile();
        var restoring = database.Path + ".restoring";
        try
        {
            File.WriteAllBytes(plain, Decompress(compressed));
            if (!LooksLikeOurDatabase(plain))
                return OperationResult.Fail("ErrorBackupInvalid");

            // 1. Copia del estado actual, por si acaso.
            Directory.CreateDirectory(database.BackupsDirectory);
            CopyEncrypted(database, Path.Combine(database.BackupsDirectory, $"pre-restore-{Stamp()}.db"));

            // 2. La copia se vuelve a cifrar con la clave de este PC.
            DeleteQuietly(restoring);
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = plain, Pooling = false }.ToString()))
            {
                source.Open();
                Execute(source, "ATTACH DATABASE $path AS encrypted KEY $key", ("$path", restoring), ("$key", SqlCipherInterceptor.RawKey(database.Key)));
                Execute(source, "SELECT sqlcipher_export('encrypted')");
                Execute(source, "DETACH DATABASE encrypted");
            }

            // 3. Se sustituye la BD y se ponen al día las migraciones (la copia puede ser de una versión anterior).
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                DeleteQuietly(database.Path + suffix);
            File.Move(restoring, database.Path);
            using var db = database.Factory.CreateDbContext();
            db.Database.Migrate();
            return OperationResult.Ok();
        }
        finally
        {
            DeleteQuietly(plain);
            DeleteQuietly(restoring);
        }
    }

    /// <summary>Copia diaria automática; se llama al arrancar. Devuelve la ruta si ha hecho una copia nueva.</summary>
    public string? RunDailyBackupIfDue()
    {
        if (database is null)
            return null;

        Directory.CreateDirectory(database.BackupsDirectory);
        var today = Path.Combine(database.BackupsDirectory, $"auto-{clock.GetLocalNow():yyyyMMdd}.db");
        if (File.Exists(today))
            return null;

        CopyEncrypted(database, today);
        foreach (var old in Directory.GetFiles(database.BackupsDirectory, "auto-*.db").Order().Reverse().Skip(AutoBackupsKept))
            DeleteQuietly(old);
        return today;
    }

    /// <summary>CFG-06: copia antes de actualizar, junto a las automáticas. Devuelve la ruta (null si la BD no es un fichero).</summary>
    public string? CreatePreUpdateCopy(string newVersion)
    {
        if (database is null)
            return null;
        Directory.CreateDirectory(database.BackupsDirectory);
        var path = Path.Combine(database.BackupsDirectory, $"pre-update-{newVersion}-{Stamp()}.db");
        CopyEncrypted(database, path);
        return path;
    }

    // --- SQLite ---

    /// <summary>Copia en caliente y consistente (API de backup de SQLite) a otro fichero cifrado con la misma clave.</summary>
    private static void CopyEncrypted(DatabaseFile database, string destination)
    {
        using var source = Open(database.Path, database.Key);
        using var target = Open(destination, database.Key);
        source.BackupDatabase(target);
    }

    /// <summary>Exporta la BD a un fichero SQLite sin cifrar (temporal, se borra enseguida).</summary>
    private static void ExportPlain(DatabaseFile database, string plainPath)
    {
        using var connection = Open(database.Path, database.Key);
        Execute(connection, "ATTACH DATABASE $path AS plaintext KEY ''", ("$path", plainPath));
        Execute(connection, "SELECT sqlcipher_export('plaintext')");
        Execute(connection, "DETACH DATABASE plaintext");
    }

    private static bool LooksLikeOurDatabase(string plainPath)
    {
        try
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = plainPath, Pooling = false, Mode = SqliteOpenMode.ReadOnly }.ToString());
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN ('Sales', '__EFMigrationsHistory')";
            return Convert.ToInt32(command.ExecuteScalar()) == 2;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>Abre un fichero cifrado con la clave en bruto, igual que la app (ver <see cref="SqlCipherInterceptor"/>).</summary>
    private static SqliteConnection Open(string path, string key)
    {
        SQLitePCL.Batteries_V2.Init();
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Pooling = false,
        }.ToString());
        connection.Open();
        Execute(connection, $"PRAGMA key = \"{SqlCipherInterceptor.RawKey(key)}\"");
        return connection;
    }

    private static void Execute(SqliteConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
            command.Parameters.AddWithValue(name, value);
        command.ExecuteNonQuery();
    }

    // --- Cifrado ---

    private static byte[] Encrypt(byte[] data, string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var nonce = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var cipher = new byte[data.Length];
        using (var aes = new AesGcm(DeriveKey(password, salt), tag.Length))
            aes.Encrypt(nonce, data, cipher, tag);
        return [.. Magic, .. salt, .. nonce, .. tag, .. cipher];
    }

    private static byte[] Decrypt(byte[] file, string password)
    {
        if (file.Length < Magic.Length + 44 || !file.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("Not a StarSeaPOS backup");

        var salt = file.AsSpan(8, 16);
        var nonce = file.AsSpan(24, 12);
        var tag = file.AsSpan(36, 16);
        var cipher = file.AsSpan(52);
        var data = new byte[cipher.Length];
        using var aes = new AesGcm(DeriveKey(password, salt.ToArray()), tag.Length);
        aes.Decrypt(nonce, cipher, tag, data);
        return data;
    }

    private static byte[] DeriveKey(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), salt, Iterations, HashAlgorithmName.SHA256, 32);

    private static byte[] Compress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            gzip.Write(data);
        return output.ToArray();
    }

    private static byte[] Decompress(byte[] data)
    {
        using var input = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        input.CopyTo(output);
        return output.ToArray();
    }

    // --- Ficheros ---

    private string Stamp() => clock.GetLocalNow().ToString("yyyyMMdd-HHmmss");

    private static string TempFile() => Path.Combine(Path.GetTempPath(), $"starseapos-{Guid.NewGuid():N}.tmp");

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // fichero temporal: si Windows aún lo tiene abierto, se queda en la carpeta temporal
        }
    }
}
