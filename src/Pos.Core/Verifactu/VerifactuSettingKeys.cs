namespace Pos.Core.Verifactu;

/// <summary>Claves de los ajustes de Verifactu (tabla Settings).</summary>
public static class VerifactuSettingKeys
{
    /// <summary>"true" = modalidad VERI*FACTU: los registros se envían a la AEAT.</summary>
    public const string Enabled = "verifactu.enabled";

    /// <summary>"Test" (entorno de pruebas, por defecto) o "Production".</summary>
    public const string Environment = "verifactu.environment";

    public const string Production = "Production";
    public const string Test = "Test";

    /// <summary>Certificado (.pfx) y su contraseña, protegidos con DPAPI.</summary>
    public const string Certificate = "verifactu.certificate";
    public const string CertificatePassword = "verifactu.certificatePassword";

    /// <summary>Identificador de esta instalación (NumeroInstalacion).</summary>
    public const string InstallationNumber = "verifactu.installation";

    /// <summary>No enviar antes de esta hora (UTC, ISO): el tiempo de espera que indica la AEAT.</summary>
    public const string NextSendAfter = "verifactu.nextSendAfter";
}
