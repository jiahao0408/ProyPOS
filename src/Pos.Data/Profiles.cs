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

/// <summary>Impresora térmica (HW-01) e impresión al cobrar (IMP-01).</summary>
public sealed record PrinterProfile(
    PrinterConnection Connection,
    string Target,
    int PaperWidthMm,
    PrinterEncoding Encoding,
    AutoPrintMode AutoPrint,
    int BaudRate = 9600)
{
    public static PrinterProfile Default { get; } = new(PrinterConnection.File, "", 80, PrinterEncoding.Western, AutoPrintMode.Ask);

    /// <summary>Caracteres por línea con la fuente normal: 32 en papel de 58 mm y 48 en papel de 80 mm.</summary>
    public int CharsPerLine => PaperWidthMm <= 58 ? 32 : 48;

    /// <summary>Ancho imprimible en puntos (203 ppp): 384 en 58 mm y 576 en 80 mm.</summary>
    public int DotsPerLine => PaperWidthMm <= 58 ? 384 : 576;
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

    public PrinterProfile GetPrinter() => Read("printer");

    public void SavePrinter(PrinterProfile p) => Write("printer", p);

    /// <summary>BAZ-01: impresora de etiquetas. Por defecto, la misma que la de tickets.</summary>
    public bool LabelsUseReceiptPrinter => settings.Get("labels.sameAsReceipt", "true") == "true";

    public PrinterProfile GetPrinter(Pos.Core.Hardware.PrinterDestination destination) =>
        destination == Pos.Core.Hardware.PrinterDestination.Labels && !LabelsUseReceiptPrinter ? Read("labels") : GetPrinter();

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
            settings.GetInt($"{prefix}.baudRate", d.BaudRate));
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
    }
}
