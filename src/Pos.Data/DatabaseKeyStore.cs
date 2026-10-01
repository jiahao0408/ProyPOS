using System.Security.Cryptography;

namespace Pos.Data;

/// <summary>
/// Clave aleatoria de la BD, guardada junto a ella. En Windows se protege con DPAPI:
/// solo la misma cuenta de Windows en el mismo PC puede descifrarla.
/// En Linux y macOS se guarda en un fichero legible solo por el usuario (pendiente: llavero del sistema).
/// </summary>
public static class DatabaseKeyStore
{
    private const int KeySize = 32;
    private static readonly byte[] Entropy = "ProyPOS.DatabaseKey.v1"u8.ToArray();

    public static string GetOrCreateKey(string keyFilePath)
    {
        if (File.Exists(keyFilePath))
            return Convert.ToHexString(Unprotect(File.ReadAllBytes(keyFilePath)));

        var key = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllBytes(keyFilePath, Protect(key));
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        return Convert.ToHexString(key);
    }

    private static byte[] Protect(byte[] data) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser)
            : data;

    private static byte[] Unprotect(byte[] data) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser)
            : data;
}
