using System.Globalization;
using System.Xml.Linq;
using Pos.Core.Domain;
using Pos.Core.Verifactu;

namespace Pos.Modules.Verifactu;

/// <summary>Un registro listo para enviar, con lo que hace falta de su factura.</summary>
public sealed record OutgoingRecord(
    VerifactuRecord Record,
    Invoice Invoice,
    VerifactuRecord? Previous,
    Invoice? Replaced,
    Invoice? Rectified = null);

/// <summary>
/// Mensaje SOAP RegFactuSistemaFacturacion del servicio web de Verifactu (VFA-02), según los esquemas
/// SuministroLR.xsd y SuministroInformacion.xsd de la AEAT. El orden de los elementos es el del esquema.
/// </summary>
public static class VerifactuXml
{
    public static readonly XNamespace Soap = "http://schemas.xmlsoap.org/soap/envelope/";
    public static readonly XNamespace Sum = "https://www2.agenciatributaria.gob.es/static_files/common/internet/dep/aplicaciones/es/aeat/tike/cont/ws/SuministroLR.xsd";
    public static readonly XNamespace Sum1 = "https://www2.agenciatributaria.gob.es/static_files/common/internet/dep/aplicaciones/es/aeat/tike/cont/ws/SuministroInformacion.xsd";

    /// <summary>Máximo de registros por envío que admite la AEAT.</summary>
    public const int MaxRecordsPerMessage = 1000;

    public static string Build(string issuerName, string issuerNif, IEnumerable<OutgoingRecord> records, ProducerInfo producer, string installationNumber)
    {
        var body = new XElement(Sum + "RegFactuSistemaFacturacion",
            new XElement(Sum + "Cabecera",
                new XElement(Sum1 + "ObligadoEmision",
                    new XElement(Sum1 + "NombreRazon", issuerName),
                    new XElement(Sum1 + "NIF", issuerNif))),
            records.Select(r => new XElement(Sum + "RegistroFactura", Alta(r, producer, installationNumber))));

        var envelope = new XElement(Soap + "Envelope",
            new XAttribute(XNamespace.Xmlns + "soapenv", Soap),
            new XAttribute(XNamespace.Xmlns + "sum", Sum),
            new XAttribute(XNamespace.Xmlns + "sum1", Sum1),
            new XElement(Soap + "Header"),
            new XElement(Soap + "Body", body));
        return new XDocument(new XDeclaration("1.0", "UTF-8", null), envelope).ToString(SaveOptions.DisableFormatting);
    }

    private static XElement Alta(OutgoingRecord o, ProducerInfo producer, string installationNumber)
    {
        var r = o.Record;
        var invoice = o.Invoice;
        return new XElement(Sum1 + "RegistroAlta",
            E("IDVersion", "1.0"),
            InvoiceId("IDFactura", r.IssuerNif, r.InvoiceNumber, r.IssueDate),
            E("NombreRazonEmisor", r.IssuerName),
            E("TipoFactura", r.InvoiceType),
            // FAC-03: rectificativa por diferencias (importes en negativo) con la factura que rectifica.
            o.Rectified is { } rectified ? E("TipoRectificativa", "I") : null,
            o.Rectified is { } original
                ? new XElement(Sum1 + "FacturasRectificadas",
                    InvoiceId("IDFacturaRectificada", original.IssuerNif, original.Code, VerifactuHash.Date(original.IssuedAtUtc.ToLocalTime())))
                : null,
            o.Replaced is { } replaced
                ? new XElement(Sum1 + "FacturasSustituidas",
                    InvoiceId("IDFacturaSustituida", replaced.IssuerNif, replaced.Code, VerifactuHash.Date(replaced.IssuedAtUtc.ToLocalTime())))
                : null,
            E("DescripcionOperacion", "Venta de mercaderías"),
            invoice.CustomerNif is { } customerNif && r.InvoiceType is not ("F2" or "R5")
                ? new XElement(Sum1 + "Destinatarios",
                    new XElement(Sum1 + "IDDestinatario",
                        E("NombreRazon", invoice.CustomerName ?? ""),
                        E("NIF", customerNif)))
                : null,
            new XElement(Sum1 + "Desglose",
                invoice.VatLines.OrderByDescending(v => v.Rate).Select(v => new XElement(Sum1 + "DetalleDesglose",
                    E("ClaveRegimen", "01"),            // régimen general
                    E("CalificacionOperacion", "S1"),   // sujeta y no exenta
                    E("TipoImpositivo", v.Rate.ToString("0.00", CultureInfo.InvariantCulture)),
                    E("BaseImponibleOimporteNoSujeto", VerifactuHash.Amount(v.Base)),
                    E("CuotaRepercutida", VerifactuHash.Amount(v.VatAmount))))),
            E("CuotaTotal", VerifactuHash.Amount(r.TotalVat)),
            E("ImporteTotal", VerifactuHash.Amount(r.Total)),
            new XElement(Sum1 + "Encadenamiento",
                o.Previous is { } previous
                    ? new XElement(Sum1 + "RegistroAnterior",
                        E("IDEmisorFactura", previous.IssuerNif),
                        E("NumSerieFactura", previous.InvoiceNumber),
                        E("FechaExpedicionFactura", previous.IssueDate),
                        E("Huella", previous.Hash))
                    : E("PrimerRegistro", "S")),
            new XElement(Sum1 + "SistemaInformatico",
                E("NombreRazon", producer.Name),
                E("NIF", producer.Nif),
                E("NombreSistemaInformatico", producer.SystemName),
                E("IdSistemaInformatico", producer.SystemId),
                E("Version", producer.Version),
                E("NumeroInstalacion", installationNumber),
                E("TipoUsoPosibleSoloVerifactu", "S"),
                E("TipoUsoPosibleMultiOT", "N"),
                E("IndicadorMultiplesOT", "N")),
            E("FechaHoraHusoGenRegistro", r.GeneratedAt),
            E("TipoHuella", "01"), // SHA-256
            E("Huella", r.Hash));
    }

    private static XElement InvoiceId(string name, string nif, string number, string date) =>
        new(Sum1 + name,
            E("IDEmisorFactura", nif),
            E("NumSerieFactura", number),
            E("FechaExpedicionFactura", date));

    private static XElement E(string name, string value) => new(Sum1 + name, value);
}

/// <summary>Respuesta de la AEAT a un envío.</summary>
public sealed record VerifactuResponse(
    string SubmissionStatus,
    int WaitSeconds,
    IReadOnlyList<VerifactuLineResult> Lines,
    string? Csv);

public sealed record VerifactuLineResult(string InvoiceNumber, string Status, string? ErrorCode, string? ErrorMessage);

public static class VerifactuResponseParser
{
    /// <summary>Tiempo de espera por defecto entre envíos si la AEAT no indica otro.</summary>
    public const int DefaultWaitSeconds = 60;

    /// <summary>Lee la respuesta; si es un SOAP Fault, lanza una excepción con su texto.</summary>
    public static VerifactuResponse Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        string? Value(XContainer parent, string name) =>
            parent.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim();

        if (doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Fault") is { } fault)
            throw new VerifactuServiceException(Value(fault, "faultstring") ?? fault.Value);

        var lines = doc.Descendants().Where(e => e.Name.LocalName == "RespuestaLinea")
            .Select(l => new VerifactuLineResult(
                Value(l, "NumSerieFactura") ?? "",
                Value(l, "EstadoRegistro") ?? "",
                Value(l, "CodigoErrorRegistro"),
                Value(l, "DescripcionErrorRegistro")))
            .ToList();

        return new VerifactuResponse(
            Value(doc, "EstadoEnvio") ?? "",
            int.TryParse(Value(doc, "TiempoEsperaEnvio"), out var wait) ? wait : DefaultWaitSeconds,
            lines,
            Value(doc, "CSV"));
    }
}

public sealed class VerifactuServiceException(string message) : Exception(message);
