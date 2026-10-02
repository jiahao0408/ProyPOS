using Pos.Core.Invoicing;

namespace Pos.Data;

/// <summary>Datos del negocio para la cabecera y el pie del ticket (IMP-03) y como emisor de las facturas.</summary>
public sealed record BusinessProfile(
    string Name,
    string Nif,
    string Address,
    string PostalCode,
    string City,
    string Phone,
    string FooterMessage,
    string? LogoPath)
{
    public static BusinessProfile Empty { get; } = new("", "", "", "", "", "", "", null);

    public string FullAddress => string.Join(", ", new[] { Address, $"{PostalCode} {City}".Trim() }.Where(s => s.Length > 0));

    /// <summary>Sin nombre, NIF válido y dirección no se puede emitir una factura legal.</summary>
    public string? ValidateFiscalData() =>
        Name.Trim().Length == 0 ? "ErrorBusinessNameRequired"
        : !NifValidator.IsValid(Nif) ? "ErrorBusinessNif"
        : Address.Trim().Length == 0 ? "ErrorBusinessAddressRequired"
        : null;
}

public enum PrinterConnection
{
    /// <summary>Impresora instalada en Windows (USB con driver o "Generic / Text Only"); se le envían datos RAW.</summary>
    Windows = 0,

    /// <summary>Puerto serie COM. Las impresoras Bluetooth emparejadas aparecen como un COM virtual.</summary>
    Serial = 1,

    /// <summary>Impresora de red, puerto 9100.</summary>
    Network = 2,

    /// <summary>Sin impresora: guarda cada ticket en un fichero (para probar).</summary>
    File = 3,
}

public enum PrinterEncoding
{
    /// <summary>Alfabeto latino con €, ñ y acentos (página de códigos PC858).</summary>
    Western = 0,

    /// <summary>Chino simplificado (GB18030). Solo en impresoras con fuente china.</summary>
    Chinese = 1,
}

public enum AutoPrintMode
{
    Yes = 0,
    No = 1,
    Ask = 2,
}

/// <summary>
/// v1.1: tabla de caracteres de la impresora (comando ESC t). No todas las térmicas traen las mismas:
/// si salen mal los acentos o el €, se prueba otra. Lo que no exista en la tabla elegida se sustituye
/// (€ → EUR, á → a).
/// </summary>
public enum PrinterCodePage
{
    /// <summary>PC858 (latín con €), ESC t 19. Epson y la mayoría de compatibles.</summary>
    Pc858 = 0,

    /// <summary>Windows-1252 (latín con €), ESC t 16.</summary>
    Wpc1252 = 1,

    /// <summary>PC850 (latín, sin €), ESC t 2.</summary>
    Pc850 = 2,

    /// <summary>PC437 (EE. UU., la tabla por defecto de casi todas), ESC t 0.</summary>
    Pc437 = 3,
}

public enum PrinterCutMode
{
    /// <summary>Corte parcial (GS V 66 0).</summary>
    Partial = 0,

    /// <summary>Corte total (GS V 65 0).</summary>
    Full = 1,

    /// <summary>Sin cortador (impresoras de 58 mm baratas): solo avanza el papel para arrancarlo.</summary>
    None = 2,
}

public enum PrinterQrMode
{
    /// <summary>Comando de QR de la impresora (GS ( k).</summary>
    Native = 0,

    /// <summary>El QR se dibuja como imagen: para impresoras que no entienden GS ( k.</summary>
    Image = 1,
}

/// <summary>Control de flujo del puerto serie (algunas impresoras COM lo necesitan para no perder datos).</summary>
public enum SerialHandshake
{
    None = 0,
    XOnXOff = 1,
    RtsCts = 2,
    DtrDsr = 3,
}

/// <summary>Presets de la página de impresora; "Custom" = lo que haya elegido el usuario.</summary>
public enum PrinterModel
{
    EpsonCompatible = 0,
    Generic80 = 1,
    Generic58NoCutter = 2,
    Custom = 3,
}

/// <summary>Protocolo del visor de cliente por puerto serie (VFD de 2 líneas × 20 caracteres).</summary>
public enum PoleDisplayProtocol
{
    /// <summary>Comandos ESC/POS de visor (Epson DM-D y compatibles).</summary>
    EscPos = 0,

    /// <summary>CD5220 (muy habitual en visores genéricos).</summary>
    Cd5220 = 1,
}

/// <summary>Impresora térmica (HW-01) e impresión al cobrar (IMP-01).</summary>
public sealed record PrinterProfile(
    PrinterConnection Connection,
    string Target,
    int PaperWidthMm,
    PrinterEncoding Encoding,
    AutoPrintMode AutoPrint,
    int BaudRate = 9600,
    PrinterCodePage CodePage = PrinterCodePage.Pc858,
    PrinterCutMode Cut = PrinterCutMode.Partial,
    PrinterQrMode Qr = PrinterQrMode.Native,
    SerialHandshake Handshake = SerialHandshake.None,
    PrinterModel Model = PrinterModel.EpsonCompatible)
{
    public static PrinterProfile Default { get; } = new(PrinterConnection.File, "", 80, PrinterEncoding.Western, AutoPrintMode.Ask);

    /// <summary>Caracteres por línea con la fuente normal: 32 en papel de 58 mm y 48 en papel de 80 mm.</summary>
    public int CharsPerLine => PaperWidthMm <= 58 ? 32 : 48;

    /// <summary>Ancho imprimible en puntos (203 ppp): 384 en 58 mm y 576 en 80 mm.</summary>
    public int DotsPerLine => PaperWidthMm <= 58 ? 384 : 576;

    /// <summary>v1.1: aplica los ajustes de compatibilidad de un modelo (tabla, corte y QR).</summary>
    public PrinterProfile WithModel(PrinterModel model) => model switch
    {
        PrinterModel.EpsonCompatible => this with { Model = model, CodePage = PrinterCodePage.Pc858, Cut = PrinterCutMode.Partial, Qr = PrinterQrMode.Native },
        PrinterModel.Generic80 => this with { Model = model, CodePage = PrinterCodePage.Pc858, Cut = PrinterCutMode.Partial, Qr = PrinterQrMode.Image },
        PrinterModel.Generic58NoCutter => this with
        {
            Model = model, PaperWidthMm = 58, CodePage = PrinterCodePage.Pc437, Cut = PrinterCutMode.None, Qr = PrinterQrMode.Image,
        },
        _ => this with { Model = PrinterModel.Custom },
    };
}

/// <summary>
/// HW-03: cajón portamonedas conectado a la impresora de tickets.
/// HW-02: pantalla de cliente en un segundo monitor, con un mensaje de bienvenida en reposo.
/// </summary>
/// <param name="DrawerPin">Conector del cajón en la impresora: 2 (el habitual) o 5.</param>
/// <param name="DrawerPort">v1.1: puerto COM de un cajón con su propio cable o disparador USB; vacío = por la impresora.</param>
/// <param name="PolePort">v1.1: puerto COM de un visor de cliente (2 × 20); vacío = sin visor.</param>
/// <param name="ScannerPort">v1.1: puerto COM de un lector de códigos serie; vacío = lector USB en modo teclado.</param>
public sealed record HardwareProfile(
    bool OpenDrawerOnCash,
    bool CustomerDisplay,
    string WelcomeMessage,
    int DrawerPin = 2,
    string DrawerPort = "",
    string PolePort = "",
    PoleDisplayProtocol PoleProtocol = PoleDisplayProtocol.EscPos,
    int PoleBaudRate = 9600,
    string ScannerPort = "",
    int ScannerBaudRate = 9600)
{
    public static HardwareProfile Default { get; } = new(false, false, "");
}

/// <summary>Lee y guarda los perfiles en la tabla de ajustes.</summary>
public sealed class ProfileStore(SettingsStore settings)
{
    public BusinessProfile GetBusiness() => new(
        settings.Get("business.name", ""),
        settings.Get("business.nif", ""),
        settings.Get("business.address", ""),
        settings.Get("business.postalCode", ""),
        settings.Get("business.city", ""),
        settings.Get("business.phone", ""),
        settings.Get("business.footer", ""),
        settings.Get("business.logoPath") is { Length: > 0 } logo ? logo : null);

    public void SaveBusiness(BusinessProfile p)
    {
        settings.Set("business.name", p.Name.Trim());
        settings.Set("business.nif", NifValidator.Normalize(p.Nif));
        settings.Set("business.address", p.Address.Trim());
        settings.Set("business.postalCode", p.PostalCode.Trim());
        settings.Set("business.city", p.City.Trim());
        settings.Set("business.phone", p.Phone.Trim());
        settings.Set("business.footer", p.FooterMessage.Trim());
        settings.Set("business.logoPath", p.LogoPath ?? "");
    }

    public HardwareProfile GetHardware() => new(
        settings.Get("drawer.openOnCash", "false") == "true",
        settings.Get("display.enabled", "false") == "true",
        settings.Get("display.welcome", ""),
        settings.GetInt("drawer.pin", 2) == 5 ? 5 : 2,
        settings.Get("drawer.port", ""),
        settings.Get("pole.port", ""),
        Enum.TryParse<PoleDisplayProtocol>(settings.Get("pole.protocol"), out var protocol) ? protocol : PoleDisplayProtocol.EscPos,
        settings.GetInt("pole.baudRate", 9600),
        settings.Get("scanner.port", ""),
        settings.GetInt("scanner.baudRate", 9600));

    public void SaveHardware(HardwareProfile p)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        settings.Set("drawer.openOnCash", p.OpenDrawerOnCash ? "true" : "false");
        settings.Set("display.enabled", p.CustomerDisplay ? "true" : "false");
        settings.Set("display.welcome", p.WelcomeMessage.Trim());
        settings.Set("drawer.pin", (p.DrawerPin == 5 ? 5 : 2).ToString(inv));
        settings.Set("drawer.port", p.DrawerPort.Trim());
        settings.Set("pole.port", p.PolePort.Trim());
        settings.Set("pole.protocol", p.PoleProtocol.ToString());
        settings.Set("pole.baudRate", p.PoleBaudRate.ToString(inv));
        settings.Set("scanner.port", p.ScannerPort.Trim());
        settings.Set("scanner.baudRate", p.ScannerBaudRate.ToString(inv));
    }

    public PrinterProfile GetPrinter() => Read("printer");

    public void SavePrinter(PrinterProfile p) => Write("printer", p);

    /// <summary>BAZ-01: impresora de etiquetas. Por defecto, la misma que la de tickets.</summary>
    public bool LabelsUseReceiptPrinter => settings.Get("labels.sameAsReceipt", "true") == "true";

    public PrinterProfile GetPrinter(Pos.Core.Hardware.PrinterDestination destination) => destination switch
    {
        Pos.Core.Hardware.PrinterDestination.Labels when !LabelsUseReceiptPrinter => Read("labels"),
        // v1.1: cajón con su propio puerto COM (los disparadores USB se instalan como COM virtual).
        Pos.Core.Hardware.PrinterDestination.Drawer when GetHardware().DrawerPort is { Length: > 0 } port =>
            PrinterProfile.Default with { Connection = PrinterConnection.Serial, Target = port },
        _ => GetPrinter(),
    };

    public void SaveLabelPrinter(bool sameAsReceipt, PrinterProfile? profile)
    {
        settings.Set("labels.sameAsReceipt", sameAsReceipt ? "true" : "false");
        if (profile is not null)
            Write("labels", profile);
    }

    public PrinterProfile GetLabelPrinterSettings() => Read("labels");

    private PrinterProfile Read(string prefix)
    {
        var d = PrinterProfile.Default;
        return new PrinterProfile(
            Enum.TryParse<PrinterConnection>(settings.Get($"{prefix}.connection"), out var c) ? c : d.Connection,
            settings.Get($"{prefix}.target", d.Target),
            settings.GetInt($"{prefix}.paperWidth", d.PaperWidthMm),
            Enum.TryParse<PrinterEncoding>(settings.Get($"{prefix}.encoding"), out var e) ? e : d.Encoding,
            Enum.TryParse<AutoPrintMode>(settings.Get($"{prefix}.autoPrint"), out var a) ? a : d.AutoPrint,
            settings.GetInt($"{prefix}.baudRate", d.BaudRate),
            Enum.TryParse<PrinterCodePage>(settings.Get($"{prefix}.codePage"), out var cp) ? cp : d.CodePage,
            Enum.TryParse<PrinterCutMode>(settings.Get($"{prefix}.cut"), out var cut) ? cut : d.Cut,
            Enum.TryParse<PrinterQrMode>(settings.Get($"{prefix}.qr"), out var qr) ? qr : d.Qr,
            Enum.TryParse<SerialHandshake>(settings.Get($"{prefix}.handshake"), out var hs) ? hs : d.Handshake,
            Enum.TryParse<PrinterModel>(settings.Get($"{prefix}.model"), out var model) ? model : d.Model);
    }

    private void Write(string prefix, PrinterProfile p)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        settings.Set($"{prefix}.connection", p.Connection.ToString());
        settings.Set($"{prefix}.target", p.Target.Trim());
        settings.Set($"{prefix}.paperWidth", p.PaperWidthMm.ToString(inv));
        settings.Set($"{prefix}.encoding", p.Encoding.ToString());
        settings.Set($"{prefix}.autoPrint", p.AutoPrint.ToString());
        settings.Set($"{prefix}.baudRate", p.BaudRate.ToString(inv));
        settings.Set($"{prefix}.codePage", p.CodePage.ToString());
        settings.Set($"{prefix}.cut", p.Cut.ToString());
        settings.Set($"{prefix}.qr", p.Qr.ToString());
        settings.Set($"{prefix}.handshake", p.Handshake.ToString());
        settings.Set($"{prefix}.model", p.Model.ToString());
    }
}
