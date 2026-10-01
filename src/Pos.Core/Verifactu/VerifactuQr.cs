using System.Globalization;

namespace Pos.Core.Verifactu;

/// <summary>
/// QR de cotejo de Verifactu (FAC-04): una URL del servicio de la AEAT con el NIF del emisor,
/// el número de factura, la fecha y el importe. Quien escanea el ticket comprueba que la
/// factura se ha comunicado a Hacienda.
/// </summary>
public static class VerifactuQr
{
    public const string ProductionUrl = "https://www2.agenciatributaria.gob.es/wlpl/TIKE-CONT/ValidarQR";
    public const string TestUrl = "https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR";

    /// <summary>Texto que acompaña al QR cuando el sistema funciona en modalidad VERI*FACTU.</summary>
    public const string VerifactuLegend = "VERI*FACTU";

    public static string Build(string issuerNif, string invoiceNumber, DateTime issueDateLocal, decimal total, bool testEnvironment)
    {
        var query = string.Join('&',
            $"nif={Uri.EscapeDataString(issuerNif.Trim())}",
            $"numserie={Uri.EscapeDataString(invoiceNumber.Trim())}",
            $"fecha={VerifactuHash.Date(issueDateLocal)}",
            $"importe={total.ToString("0.00", CultureInfo.InvariantCulture)}");
        return $"{(testEnvironment ? TestUrl : ProductionUrl)}?{query}";
    }

    /// <summary>Saca el número de factura de un QR leído con el lector (para buscar el ticket, FAC-06).</summary>
    public static string? InvoiceNumberFrom(string scanned)
    {
        var start = scanned.IndexOf('?');
        if (start < 0)
            return null;
        foreach (var pair in scanned[(start + 1)..].Split('&'))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 && parts[0].Equals("numserie", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(parts[1]);
        }
        return null;
    }
}
