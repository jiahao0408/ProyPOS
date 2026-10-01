using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Pos.Core;
using Pos.Core.Verifactu;
using Pos.Data;

namespace Pos.Modules.Verifactu;

public sealed record CertificateInfo(string Subject, string? Nif, DateTime ValidUntil);

/// <summary>
/// VFA-01: certificado digital del negocio (.pfx). Se guarda cifrado con DPAPI (solo esta cuenta de
/// Windows en este PC puede leerlo), y además dentro de la BD, que también va cifrada.
/// </summary>
public sealed partial class CertificateStore(SettingsStore settings, TimeProvider clock)
{
    private static readonly byte[] Entropy = "StarSeaPOS.Verifactu.v1"u8.ToArray();

    public OperationResult<CertificateInfo> Save(byte[] pfx, string password)
    {
        X509Certificate2 certificate;
        try
        {
            certificate = new X509Certificate2(pfx, password, X509KeyStorageFlags.UserKeySet);
        }
        catch (CryptographicException)
        {
            return OperationResult<CertificateInfo>.Fail("ErrorCertificatePassword");
        }

        using (certificate)
        {
            if (!certificate.HasPrivateKey)
                return OperationResult<CertificateInfo>.Fail("ErrorCertificateNoKey");
            if (certificate.NotAfter < clock.GetLocalNow().DateTime)
                return OperationResult<CertificateInfo>.Fail("ErrorCertificateExpired");

            settings.Set(VerifactuSettingKeys.Certificate, Protect(pfx));
            settings.Set(VerifactuSettingKeys.CertificatePassword, Protect(Encoding.UTF8.GetBytes(password)));
            return OperationResult<CertificateInfo>.Ok(Describe(certificate));
        }
    }

    public bool HasCertificate => settings.Get(VerifactuSettingKeys.Certificate) is { Length: > 0 };

    /// <summary>El certificado con su clave privada, para la conexión con la AEAT. Hay que liberarlo después.</summary>
    public X509Certificate2? Load()
    {
        if (settings.Get(VerifactuSettingKeys.Certificate) is not { Length: > 0 } pfx
            || settings.Get(VerifactuSettingKeys.CertificatePassword) is not { } password)
            return null;
        return new X509Certificate2(Unprotect(pfx), Encoding.UTF8.GetString(Unprotect(password)), X509KeyStorageFlags.UserKeySet);
    }

    public CertificateInfo? GetInfo()
    {
        using var certificate = Load();
        return certificate is null ? null : Describe(certificate);
    }

    public void Remove()
    {
        settings.Set(VerifactuSettingKeys.Certificate, "");
        settings.Set(VerifactuSettingKeys.CertificatePassword, "");
    }

    /// <summary>
    /// El NIF va en el sujeto del certificado: SERIALNUMBER=IDCES-12345678Z en personas físicas,
    /// o 2.5.4.97 (organizationIdentifier) = VATES-B12345674 en certificados de representante.
    /// </summary>
    public static CertificateInfo Describe(X509Certificate2 certificate)
    {
        var subject = certificate.Subject;
        var nif = OrganizationNif().Match(subject) is { Success: true } org ? org.Groups[1].Value
            : PersonNif().Match(subject) is { Success: true } person ? person.Groups[1].Value
            : null;
        return new CertificateInfo(certificate.GetNameInfo(X509NameType.SimpleName, false), nif, certificate.NotAfter);
    }

    private static string Protect(byte[] data) =>
        Convert.ToBase64String(ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser));

    private static byte[] Unprotect(string data) =>
        ProtectedData.Unprotect(Convert.FromBase64String(data), Entropy, DataProtectionScope.CurrentUser);

    [GeneratedRegex(@"(?:OID\.2\.5\.4\.97|organizationIdentifier)=VATES-([A-Z0-9]{9})", RegexOptions.IgnoreCase)]
    private static partial Regex OrganizationNif();

    [GeneratedRegex(@"SERIALNUMBER=(?:IDCES-)?([A-Z0-9]{9})", RegexOptions.IgnoreCase)]
    private static partial Regex PersonNif();
}
