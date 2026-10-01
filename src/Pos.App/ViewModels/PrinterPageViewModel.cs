using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Localization;
using Pos.Data;
using Pos.Modules.Hardware;
using Pos.Modules.Printing;

namespace Pos.App.ViewModels;

/// <summary>Opción de una lista desplegable (para las plantillas de XAML, que no admiten genéricos).</summary>
public interface IChoice
{
    string Title { get; }
}

public sealed record Choice<T>(T Value, string Title) : IChoice;

/// <summary>HW-01: impresora térmica (USB, COM/Bluetooth o red), papel de 58 u 80 mm y prueba de impresión. IMP-01: impresión al cobrar.</summary>
public partial class PrinterPageViewModel(ILocalizer localizer, ProfileStore profiles, PrintService printing)
    : PageViewModel(localizer)
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetHint), nameof(ShowBaudRate))]
    private Choice<PrinterConnection>? _connection;

    [ObservableProperty]
    private string _target = "";

    [ObservableProperty]
    private IReadOnlyList<string> _targetSuggestions = [];

    [ObservableProperty]
    private string _baudRateText = "9600";

    [ObservableProperty]
    private Choice<int>? _paperWidth;

    [ObservableProperty]
    private Choice<PrinterEncoding>? _encoding;

    [ObservableProperty]
    private Choice<AutoPrintMode>? _autoPrint;

    [ObservableProperty]
    private bool _isTesting;

    // --- BAZ-01: impresora de etiquetas ---

    [ObservableProperty]
    private bool _labelsSameAsReceipt = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LabelTargetHint))]
    private Choice<PrinterConnection>? _labelConnection;

    [ObservableProperty]
    private string _labelTarget = "";

    [ObservableProperty]
    private Choice<int>? _labelPaperWidth;

    public string LabelTargetHint => L[LabelConnection?.Value switch
    {
        PrinterConnection.Windows => "PrinterTargetWindows",
        PrinterConnection.Serial => "PrinterTargetSerial",
        PrinterConnection.Network => "PrinterTargetNetwork",
        _ => "PrinterTargetFile",
    }];

    public IReadOnlyList<Choice<PrinterConnection>> Connections { get; private set; } = [];

    public IReadOnlyList<Choice<int>> PaperWidths { get; } = [new(80, "80 mm"), new(58, "58 mm")];

    public IReadOnlyList<Choice<PrinterEncoding>> Encodings { get; private set; } = [];

    public IReadOnlyList<Choice<AutoPrintMode>> AutoPrintModes { get; private set; } = [];

    public string TargetHint => L[Connection?.Value switch
    {
        PrinterConnection.Windows => "PrinterTargetWindows",
        PrinterConnection.Serial => "PrinterTargetSerial",
        PrinterConnection.Network => "PrinterTargetNetwork",
        _ => "PrinterTargetFile",
    }];

    public bool ShowBaudRate => Connection?.Value == PrinterConnection.Serial;

    /// <summary>Última prueba lanzada (para esperarla en los tests).</summary>
    public Task LastTest { get; private set; } = Task.CompletedTask;

    public override void Load()
    {
        Connections =
        [
            new(PrinterConnection.Windows, L["PrinterUsbWindows"]),
            new(PrinterConnection.Serial, L["PrinterSerial"]),
            new(PrinterConnection.Network, L["PrinterNetwork"]),
            new(PrinterConnection.File, L["PrinterFile"]),
        ];
        Encodings = [new(PrinterEncoding.Western, L["EncodingWestern"]), new(PrinterEncoding.Chinese, L["EncodingChinese"])];
        AutoPrintModes = [new(AutoPrintMode.Yes, L["Yes"]), new(AutoPrintMode.No, L["No"]), new(AutoPrintMode.Ask, L["AskEachTime"])];
        OnPropertyChanged(nameof(Connections));
        OnPropertyChanged(nameof(Encodings));
        OnPropertyChanged(nameof(AutoPrintModes));

        var p = profiles.GetPrinter();
        Connection = Connections.First(c => c.Value == p.Connection);
        Target = p.Target;
        BaudRateText = p.BaudRate.ToString(L.Culture);
        PaperWidth = PaperWidths.FirstOrDefault(w => w.Value == p.PaperWidthMm) ?? PaperWidths[0];
        Encoding = Encodings.First(e => e.Value == p.Encoding);
        AutoPrint = AutoPrintModes.First(a => a.Value == p.AutoPrint);

        var labels = profiles.GetLabelPrinterSettings();
        LabelsSameAsReceipt = profiles.LabelsUseReceiptPrinter;
        LabelConnection = Connections.First(c => c.Value == labels.Connection);
        LabelTarget = labels.Target;
        LabelPaperWidth = PaperWidths.FirstOrDefault(w => w.Value == labels.PaperWidthMm) ?? PaperWidths[1];
    }

    partial void OnConnectionChanged(Choice<PrinterConnection>? value)
    {
        TargetSuggestions = value?.Value switch
        {
            PrinterConnection.Windows => SafeList(RawPrinter.InstalledPrinters),
            PrinterConnection.Serial => SafeList(RawPrinter.SerialPorts),
            PrinterConnection.File => [RawPrinter.DefaultOutputFolder],
            _ => [],
        };
    }

    [RelayCommand]
    private void Save()
    {
        if (Current() is { } profile)
        {
            profiles.SavePrinter(profile);
            profiles.SaveLabelPrinter(LabelsSameAsReceipt, LabelsSameAsReceipt ? null : profile with
            {
                Connection = LabelConnection?.Value ?? PrinterConnection.File,
                Target = LabelTarget.Trim(),
                PaperWidthMm = LabelPaperWidth?.Value ?? 58,
            });
            ShowInfo("Saved");
        }
    }

    /// <summary>HW-01: guarda y manda un ticket de prueba.</summary>
    [RelayCommand]
    private void Test()
    {
        if (Current() is not { } profile)
            return;
        profiles.SavePrinter(profile);
        LastTest = TestAsync();
    }

    private async Task TestAsync()
    {
        IsTesting = true;
        var outcome = await printing.PrintTestAsync();
        IsTesting = false;
        if (outcome.Success)
            ShowInfo("PrinterTestSent");
        else
        {
            Message = string.Format(L["ErrorPrintFailed"], outcome.Error);
            MessageIsError = true;
        }
    }

    private PrinterProfile? Current()
    {
        var connection = Connection?.Value ?? PrinterConnection.File;
        if (connection != PrinterConnection.File && string.IsNullOrWhiteSpace(Target))
        {
            ShowError("ErrorPrinterTargetRequired");
            return null;
        }
        if (!int.TryParse(BaudRateText, out var baud) || baud <= 0)
        {
            ShowError("ErrorNumberFormat");
            return null;
        }
        return new PrinterProfile(connection, Target.Trim(), PaperWidth?.Value ?? 80,
            Encoding?.Value ?? PrinterEncoding.Western, AutoPrint?.Value ?? AutoPrintMode.Ask, baud);
    }

    private static IReadOnlyList<string> SafeList(Func<IReadOnlyList<string>> list)
    {
        try
        {
            return list();
        }
        catch (Exception)
        {
            return []; // sin servicio de impresión o sin puertos: se escribe a mano
        }
    }
}
