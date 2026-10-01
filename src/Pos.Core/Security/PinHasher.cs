using System.Security.Cryptography;

namespace Pos.Core.Security;

/// <summary>
/// Hash de PIN con PBKDF2-SHA256 y sal aleatoria. Formato: "pbkdf2$iteraciones$sal$hash" (Base64).
/// Un PIN de 4 dígitos solo tiene 10.000 combinaciones, así que el hash no basta por sí solo:
/// la protección real es el cifrado de la base de datos y el bloqueo tras 5 intentos.
/// </summary>
public static class PinHasher
{
    private const string Scheme = "pbkdf2";
    private const int Iterations = 100_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public static bool IsValidPin(string? pin) =>
        pin is { Length: 4 } && pin.All(char.IsAsciiDigit);

    public static string Hash(string pin)
    {
        if (!IsValidPin(pin))
            throw new ArgumentException("El PIN debe tener exactamente 4 dígitos.", nameof(pin));

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Scheme}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string pin, string storedHash)
    {
        if (!IsValidPin(pin))
            return false;

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations))
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[2]);
            var expected = Convert.FromBase64String(parts[3]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(pin, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
