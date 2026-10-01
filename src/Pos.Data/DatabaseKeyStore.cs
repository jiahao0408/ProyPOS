using System.Security.Cryptography;

namespace Pos.Data;

/// <summary>
/// Clave aleatoria de la BD, guardada junto a ella y protegida con DPAPI:
/// solo la misma cuenta de Windows en el mismo PC puede descifrarla.
/// </summary>
public static class DatabaseKeyStore
{
    private const int KeySize = 32;
    private static readonly byte[] Entropy = "StarSeaPOS.DatabaseKey.v1"u8.ToArray();

    public static string GetOrCreateKey(string keyFilePath)
    {
        if (File.Exists(keyFilePath))
        {
            var stored = File.ReadAllBytes(keyFilePath);
            return Convert.ToHexString(ProtectedData.Unprotect(stored, Entropy, DataProtectionScope.CurrentUser));
        }

        var key = RandomNumberGenerator.GetBytes(KeySize);
        File.WriteAllBytes(keyFilePath, ProtectedData.Protect(key, Entropy, DataProtectionScope.CurrentUser));
        return Convert.ToHexString(key);
    }
}
