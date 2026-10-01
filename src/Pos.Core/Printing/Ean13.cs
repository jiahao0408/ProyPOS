namespace Pos.Core.Printing;

/// <summary>
/// Códigos EAN-13. Los productos que llegan sin código reciben uno interno que empieza por "2"
/// (rango 20-29, reservado para uso dentro de la tienda: nunca coincide con un código de fábrica).
/// </summary>
public static class Ean13
{
    public static bool IsValid(string? code) =>
        code is { Length: 13 } && code.All(char.IsAsciiDigit) && CheckDigit(code[..12]) == code[12] - '0';

    public static int CheckDigit(string first12)
    {
        var sum = 0;
        for (var i = 0; i < 12; i++)
            sum += (first12[i] - '0') * (i % 2 == 0 ? 1 : 3);
        return (10 - sum % 10) % 10;
    }

    /// <summary>Código interno para un producto: "29" + id con 10 cifras + dígito de control.</summary>
    public static string Internal(int productId)
    {
        var first12 = "29" + productId.ToString("D10");
        return first12 + CheckDigit(first12);
    }
}
