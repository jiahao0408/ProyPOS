namespace Pos.Core.Invoicing;

/// <summary>
/// FAC-02: valida el formato del NIF español con su dígito o letra de control.
/// Acepta DNI (12345678Z), NIE (X1234567L), NIF de persona jurídica (B12345674)
/// y NIF especiales K, L y M.
/// </summary>
public static class NifValidator
{
    private const string DniLetters = "TRWAGMYFPDXBNJZSQVHLCKE";
    private const string CifControlLetters = "JABCDEFGHI";
    private const string CifEntityLetters = "ABCDEFGHJNPQRSUVW";

    /// <summary>Quita espacios, guiones y puntos, pasa a mayúsculas y quita el prefijo "ES".</summary>
    public static string Normalize(string? nif)
    {
        var clean = new string((nif ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        return clean.Length == 11 && clean.StartsWith("ES") ? clean[2..] : clean;
    }

    public static bool IsValid(string? nif)
    {
        var n = Normalize(nif);
        if (n.Length != 9)
            return false;

        return n[0] switch
        {
            >= '0' and <= '9' => IsValidDni(n),
            'X' or 'Y' or 'Z' => IsValidDni((n[0] - 'X').ToString() + n[1..]),
            'K' or 'L' or 'M' => IsValidDni("0" + n[1..]),
            _ when CifEntityLetters.Contains(n[0]) => IsValidCif(n),
            _ => false,
        };
    }

    private static bool IsValidDni(string n) =>
        n[..8].All(char.IsAsciiDigit) && DniLetters[int.Parse(n[..8]) % 23] == n[8];

    private static bool IsValidCif(string n)
    {
        var digits = n[1..8];
        if (!digits.All(char.IsAsciiDigit))
            return false;

        var sum = 0;
        for (var i = 0; i < 7; i++)
        {
            var d = digits[i] - '0';
            if (i % 2 == 0)
            {
                d *= 2;
                d = d / 10 + d % 10;
            }
            sum += d;
        }
        var controlDigit = (10 - sum % 10) % 10;
        var control = n[8];

        // Unas entidades llevan siempre letra de control, otras siempre número; el resto, cualquiera.
        return n[0] switch
        {
            'P' or 'Q' or 'R' or 'S' or 'N' or 'W' => control == CifControlLetters[controlDigit],
            'A' or 'B' or 'E' or 'H' => control == (char)('0' + controlDigit),
            _ => control == CifControlLetters[controlDigit] || control == (char)('0' + controlDigit),
        };
    }
}
