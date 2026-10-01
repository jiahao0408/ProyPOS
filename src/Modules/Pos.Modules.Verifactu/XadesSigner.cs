using System.Globalization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;

namespace Pos.Modules.Verifactu;

/// <summary>
/// VFA-04: firma XAdES enveloped (XAdES-BES: RSA-SHA256, hora de firma y huella del certificado
/// firmante) de un registro de facturación, como exige la modalidad No VERI*FACTU.
/// </summary>
public static class XadesSigner
{
    public const string XadesNamespace = "http://uri.etsi.org/01903/v1.3.2#";
    private const string SignedPropertiesType = "http://uri.etsi.org/01903#SignedProperties";

    /// <summary>Devuelve el XML del registro con la firma dentro.</summary>
    public static string Sign(XElement registro, X509Certificate2 certificate, DateTimeOffset signingTime)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        doc.LoadXml(registro.ToString(SaveOptions.DisableFormatting));

        var id = Guid.NewGuid().ToString("N")[..12];
        var signatureId = $"Signature-{id}";
        var propertiesId = $"SignedProperties-{id}";

        using var key = certificate.GetRSAPrivateKey() ?? throw new CryptographicException("El certificado no tiene clave RSA.");
        var qualifying = QualifyingProperties(signatureId, propertiesId, certificate, signingTime);
        var signed = new XadesSignedXml(doc, qualifying) { SigningKey = key };
        signed.Signature.Id = signatureId;
        signed.SignedInfo!.SignatureMethod = SignedXml.XmlDsigRSASHA256Url;
        signed.SignedInfo.CanonicalizationMethod = SignedXml.XmlDsigExcC14NTransformUrl;

        // El registro completo (enveloped: sin la propia firma).
        var document = new Reference("") { DigestMethod = SignedXml.XmlDsigSHA256Url };
        document.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        document.AddTransform(new XmlDsigExcC14NTransform());
        signed.AddReference(document);

        // XAdES: propiedades firmadas (hora y certificado firmante).
        signed.AddObject(new DataObject { Data = qualifying.ChildNodes });
        var properties = new Reference($"#{propertiesId}") { DigestMethod = SignedXml.XmlDsigSHA256Url, Type = SignedPropertiesType };
        properties.AddTransform(new XmlDsigExcC14NTransform());
        signed.AddReference(properties);

        var keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(certificate));
        signed.KeyInfo = keyInfo;

        signed.ComputeSignature();
        doc.DocumentElement!.AppendChild(doc.ImportNode(signed.GetXml(), deep: true));
        return doc.OuterXml;
    }

    /// <summary>Comprueba la firma con el certificado que lleva dentro. Si algo del registro cambió, falla.</summary>
    public static bool Verify(string signedXml)
    {
        try
        {
            var doc = new XmlDocument { PreserveWhitespace = true };
            doc.LoadXml(signedXml);
            var signatureNode = doc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl).OfType<XmlElement>().FirstOrDefault();
            if (signatureNode is null)
                return false;
            var signed = new SignedXml(doc);
            signed.LoadXml(signatureNode);
            var certificate = signed.KeyInfo?.OfType<KeyInfoX509Data>().SelectMany(d => d.Certificates?.OfType<X509Certificate2>() ?? []).FirstOrDefault();
            return certificate is not null && signed.CheckSignature(certificate, verifySignatureOnly: true);
        }
        catch (Exception e) when (e is XmlException or CryptographicException)
        {
            return false;
        }
    }

    /// <summary>
    /// SignedXml solo busca los Id en el documento; las propiedades XAdES aún no están en él al firmar
    /// (van dentro de la propia firma), así que también se buscan ahí.
    /// </summary>
    private sealed class XadesSignedXml(XmlDocument document, XmlElement qualifying) : SignedXml(document)
    {
        public override XmlElement? GetIdElement(XmlDocument? doc, string id) =>
            base.GetIdElement(doc, id)
            ?? qualifying.SelectSingleNode($"//*[@Id='{id}']") as XmlElement;
    }

    private static XmlElement QualifyingProperties(string signatureId, string propertiesId, X509Certificate2 certificate, DateTimeOffset signingTime)
    {
        var doc = new XmlDocument { PreserveWhitespace = true };
        var ds = SignedXml.XmlDsigNamespaceUrl;
        XmlElement X(string name) => doc.CreateElement("xades", name, XadesNamespace);
        XmlElement D(string name) => doc.CreateElement("ds", name, ds);

        var root = X("QualifyingProperties");
        root.SetAttribute("Target", $"#{signatureId}");
        var properties = X("SignedProperties");
        properties.SetAttribute("Id", propertiesId);
        root.AppendChild(properties);

        var signatureProperties = X("SignedSignatureProperties");
        properties.AppendChild(signatureProperties);
        var time = X("SigningTime");
        time.InnerText = signingTime.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);
        signatureProperties.AppendChild(time);

        var signingCertificate = X("SigningCertificate");
        var cert = X("Cert");
        var digest = X("CertDigest");
        var method = D("DigestMethod");
        method.SetAttribute("Algorithm", SignedXml.XmlDsigSHA256Url);
        var value = D("DigestValue");
        value.InnerText = Convert.ToBase64String(SHA256.HashData(certificate.RawData));
        digest.AppendChild(method);
        digest.AppendChild(value);
        var issuerSerial = X("IssuerSerial");
        var issuer = D("X509IssuerName");
        issuer.InnerText = certificate.Issuer;
        var serial = D("X509SerialNumber");
        serial.InnerText = new System.Numerics.BigInteger(certificate.GetSerialNumber(), isUnsigned: true).ToString(CultureInfo.InvariantCulture);
        issuerSerial.AppendChild(issuer);
        issuerSerial.AppendChild(serial);
        cert.AppendChild(digest);
        cert.AppendChild(issuerSerial);
        signingCertificate.AppendChild(cert);
        signatureProperties.AppendChild(signingCertificate);

        var wrapper = doc.CreateElement("wrapper");
        wrapper.AppendChild(root);
        doc.AppendChild(wrapper);
        return wrapper;
    }
}
