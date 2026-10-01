using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Pos.Core.Verifactu;

/// <summary>
/// Huella (hash) de los registros de facturación de Verifactu (FAC-04), según la Orden HAC/1177/2024
/// y la especificación técnica de la AEAT: SHA-256 de una cadena "campo=valor&amp;…" con los valores
/// tal como van en el XML, en hexadecimal y mayúsculas. Cada registro incluye la huella del anterior:
/// eso es el encadenamiento que impide borrar o cambiar facturas sin que se note.
/// </summary>
public static class VerifactuHash
{
    /// <summary>Huella de un registro de alta.</summary>
    /// <param name="previousHash">Huella del registro anterior; vacío en el primer registro.</param>
    public static string ForAlta(
        string issuerNif,
        string invoiceNumber,
        string issueDate,
        string invoiceType,
        decimal totalVat,
        decimal total,
        string? previousHash,
        string generatedAt) =>
        Sha256(
            $"IDEmisorFactura={issuerNif.Trim()}" +
            $"&NumSerieFactura={invoiceNumber.Trim()}" +
            $"&FechaExpedicionFactura={issueDate.Trim()}" +
            $"&TipoFactura={invoiceType.Trim()}" +
            $"&CuotaTotal={Amount(totalVat)}" +
            $"&ImporteTotal={Amount(total)}" +
            $"&Huella={previousHash?.Trim()}" +
            $"&FechaHoraHusoGenRegistro={generatedAt.Trim()}");

    /// <summary>Huella de un registro de anulación.</summary>
    public static string ForAnulacion(string issuerNif, string invoiceNumber, string issueDate, string? previousHash, string generatedAt) =>
        Sha256(
            $"IDEmisorFacturaAnulada={issuerNif.Trim()}" +
            $"&NumSerieFacturaAnulada={invoiceNumber.Trim()}" +
            $"&FechaExpedicionFacturaAnulada={issueDate.Trim()}" +
            $"&Huella={previousHash?.Trim()}" +
            $"&FechaHoraHusoGenRegistro={generatedAt.Trim()}");

    /// <summary>Importe como va en el XML: punto decimal y dos decimales.</summary>
    public static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Fecha de expedición: dd-MM-yyyy.</summary>
    public static string Date(DateTime localDate) => localDate.ToString("dd-MM-yyyy", CultureInfo.InvariantCulture);

    /// <summary>Fecha, hora y huso de generación del registro: 2024-01-01T19:20:30+01:00.</summary>
    public static string Timestamp(DateTimeOffset moment) => moment.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    private static string Sha256(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
}
