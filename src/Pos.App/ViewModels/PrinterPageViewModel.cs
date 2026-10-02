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

/// <summary>
/// HW-01: impresora térmica (USB, COM/Bluetooth o red), papel de 58 u 80 mm y prueba de impresión. IMP-01: impresión al cobrar.
/// v1.1: compatibilidad (modelo, tabla de caracteres, corte, QR, control de flujo), cajón por pin o puerto propio,
/// visor de cliente por puerto serie y lector de códigos por puerto COM.
/// </summary>
public partial class PrinterPageViewModel(ILocalizer localizer, ProfileStore profiles, PrintService printing,
    CustomerDisplayViewModel customerDisplay, SerialScanner scanner)
    : PageViewModel(localizer)
{
    private bool _applyingModel;

    // --- v1.1: compatibilidad de la impresora ---

    [ObservableProperty]
    private Choice<PrinterModel>? _model;

    [ObservableProperty]
    private Choice<PrinterCodePage>? _codePage;

    [ObservableProperty]
    private Choice<PrinterCutMode>? _cut;

    [ObservableProperty]
    private Choice<PrinterQrMode>? _qr;

    [ObservableProperty]
    private Choice<SerialHandshake>? _handshake;

    public IReadOnlyList<Choice<PrinterModel>> Models { get; private set; } = [];

    public IReadOnlyList<Choice<PrinterCodePage>> CodePages { get; private set; } = [];

    public IReadOnlyList<Choice<PrinterCutMode>> CutModes { get; private set; } = [];

    public IReadOnlyList<Choice<PrinterQrMode>> QrModes { get; private set; } = [];

    public IReadOnlyList<Choice<SerialHandshake>> Handshakes { get; private set; } = [];

    // --- v1.1: cajón, visor y lector ---

    [ObservableProperty]
    private Choice<int>? _drawerPin;

    [ObservableProperty]
    private string _drawerPort = "";

    [ObservableProperty]
    private string _polePort = "";

    [ObservableProperty]
    private Choice<PoleDisplayProtocol>? _poleProtocol;

    [ObservableProperty]
    private string _poleBaudText = "9600";

    [ObservableProperty]
    private string _scannerPort = "";

    [ObservableProperty]
    private string _scannerBaudText = "9600";

    [ObservableProperty]
    private string _scannerStatus = "";

    public IReadOnlyList<Choice<int>> DrawerPins { get; } = [new(2, "2"), new(5, "5")];

    public IReadOnlyList<Choice<PoleDisplayProtocol>> PoleProtocols { get; } =
        [new(PoleDisplayProtocol.EscPos, "ESC/POS (Epson DM-D)"), new(PoleDisplayProtocol.Cd5220, "CD5220")];

    /// <summary>Puertos COM del PC, para elegirlos en el cajón, el visor y el lector.</summary>
    [ObservableProperty]
    private IReadOnlyList<string> _comPorts = [];

    partial void OnModelChanged(Choice<PrinterModel>? value)
    {
        if (_applyingModel || value is null || value.Value == PrinterModel.Custom)
            return;
        var preset = PrinterProfile.Default.WithModel(value.Value);
        _applyingModel = true;
        CodePage = CodePages.First(c => c.Value == preset.CodePage);
        Cut = CutModes.First(c => c.Value == preset.Cut);
        Qr = QrModes.First(q => q.Value == preset.Qr);
        if (value.Value == PrinterModel.Generic58NoCutter)
            PaperWidth = PaperWidths.First(w => w.Value == 58);
        _applyingModel = false;
    }

    // Tocar un ajuste a mano convierte el modelo en "personalizado".
    partial void OnCodePageChanged(Choice<PrinterCodePage>? value) => MarkCustom();

    partial void OnCutChanged(Choice<PrinterCutMode>? value) => MarkCustom();

    partial void OnQrChanged(Choice<PrinterQrMode>? value) => MarkCustom();

    private void MarkCustom()
    {
        if (!_applyingModel && Models.Count > 0)
        {
            _applyingModel = true;
            Model = Models.First(m => m.Value == PrinterModel.Custom);
            _applyingModel = false;
        }
    }

    // --- HW-03 cajón y HW-02 pantalla de cliente ---

    [ObservableProperty]
    private bool _openDrawerOnCash;

    [ObservableProperty]
    private bool _customerDisplayEnabled;

    [ObservableProperty]
    private string _welcomeMessage = "";

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
        Models =
        [
            new(PrinterModel.EpsonCompatible, L["ModelEpson"]),
            new(PrinterModel.Generic80, L["ModelGeneric80"]),
            new(PrinterModel.Generic58NoCutter, L["ModelGeneric58"]),
            new(PrinterModel.Custom, L["ModelCustom"]),
        ];
        CodePages =
        [
            new(PrinterCodePage.Pc858, "PC858 (€)"),
            new(PrinterCodePage.Wpc1252, "Windows-1252 (€)"),
            new(PrinterCodePage.Pc850, L["CodePage850"]),
            new(PrinterCodePage.Pc437, L["CodePage437"]),
        ];
        CutModes = [new(PrinterCutMode.Partial, L["CutPartial"]), new(PrinterCutMode.Full, L["CutFull"]), new(PrinterCutMode.None, L["CutNone"])];
        QrModes = [new(PrinterQrMode.Native, L["QrNative"]), new(PrinterQrMode.Image, L["QrImage"])];
        Handshakes =
        [
            new(SerialHandshake.None, L["HandshakeNone"]),
            new(SerialHandshake.XOnXOff, "XON/XOFF"),
            new(SerialHandshake.RtsCts, "RTS/CTS"),
            new(SerialHandshake.DtrDsr, "DTR/DSR"),
        ];
        foreach (var name in new[] { nameof(Models), nameof(CodePages), nameof(CutModes), nameof(QrModes), nameof(Handshakes) })
            OnPropertyChanged(name);
        ComPorts = SafeList(RawPrinter.SerialPorts);

        var p = profiles.GetPrinter();
        Connection = Connections.First(c => c.Value == p.Connection);
        Target = p.Target;
        BaudRateText = p.BaudRate.ToString(L.Culture);
        PaperWidth = PaperWidths.FirstOrDefault(w => w.Value == p.PaperWidthMm) ?? PaperWidths[0];
        Encoding = Encodings.First(e => e.Value == p.Encoding);
        AutoPrint = AutoPrintModes.First(a => a.Value == p.AutoPrint);
        _applyingModel = true;
        Model = Models.First(m => m.Value == p.Model);
        CodePage = CodePages.First(c => c.Value == p.CodePage);
        Cut = CutModes.First(c => c.Value == p.Cut);
        Qr = QrModes.First(q => q.Value == p.Qr);
        Handshake = Handshakes.First(h => h.Value == p.Handshake);
        _applyingModel = false;

        var labels = profiles.GetLabelPrinterSettings();
        LabelsSameAsReceipt = profiles.LabelsUseReceiptPrinter;
        LabelConnection = Connections.First(c => c.Value == labels.Connection);
        LabelTarget = labels.Target;
        LabelPaperWidth = PaperWidths.FirstOrDefault(w => w.Value == labels.PaperWidthMm) ?? PaperWidths[1];

        var hardware = profiles.GetHardware();
        OpenDrawerOnCash = hardware.OpenDrawerOnCash;
        CustomerDisplayEnabled = hardware.CustomerDisplay;
        WelcomeMessage = hardware.WelcomeMessage;
        DrawerPin = DrawerPins.First(d => d.Value == hardware.DrawerPin);
        DrawerPort = hardware.DrawerPort;
        PolePort = hardware.PolePort;
        PoleProtocol = PoleProtocols.First(p2 => p2.Value == hardware.PoleProtocol);
        PoleBaudText = hardware.PoleBaudRate.ToString(L.Culture);
        ScannerPort = hardware.ScannerPort;
        ScannerBaudText = hardware.ScannerBaudRate.ToString(L.Culture);
        RefreshScannerStatus();
    }

    private void RefreshScannerStatus() => ScannerStatus = ScannerPort.Trim().Length == 0 ? L["ScannerKeyboard"]
        : scanner.IsRunning ? string.Format(L["ScannerListening"], ScannerPort.Trim())
        : string.Format(L["ScannerPortError"], ScannerPort.Trim(), scanner.LastError);

    /// <summary>v1.1: manda un texto de prueba al visor de cliente.</summary>
    [RelayCommand]
    private void TestPole()
    {
        if (!SaveHardware())
            return;
        LastTest = TestPoleAsync();
    }

    private async Task TestPoleAsync()
    {
        await customerDisplay.LastPole;
        customerDisplay.ShowWelcome();
        await customerDisplay.LastPole;
        ShowInfo("PoleTestSent");
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
            if (!SaveHardware())
                return;
            ShowInfo("Saved");
        }
    }

    /// <summary>Cajón, pantalla y visor de cliente y lector. Reinicia el lector COM con el puerto nuevo.</summary>
    private bool SaveHardware()
    {
        if (!int.TryParse(PoleBaudText, out var poleBaud) || poleBaud <= 0 || !int.TryParse(ScannerBaudText, out var scannerBaud) || scannerBaud <= 0)
        {
            ShowError("ErrorNumberFormat");
            return false;
        }
        profiles.SaveHardware(new HardwareProfile(OpenDrawerOnCash, CustomerDisplayEnabled, WelcomeMessage,
            DrawerPin?.Value ?? 2, DrawerPort.Trim(), PolePort.Trim(), PoleProtocol?.Value ?? PoleDisplayProtocol.EscPos, poleBaud,
            ScannerPort.Trim(), scannerBaud));
        customerDisplay.IsEnabled = CustomerDisplayEnabled; // abre o cierra la ventana del segundo monitor
        customerDisplay.ShowWelcome();
        scanner.Restart();
        RefreshScannerStatus();
        return true;
    }

    /// <summary>HW-03: prueba del cajón.</summary>
    [RelayCommand]
    private void TestDrawer() => LastTest = TestDrawerAsync();

    private async Task TestDrawerAsync()
    {
        var outcome = await printing.OpenDrawerAsync();
        if (outcome.Success)
            ShowInfo("DrawerTestSent");
        else
        {
            Message = string.Format(L["ErrorDrawerFailed"], outcome.Error);
            MessageIsError = true;
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
            Encoding?.Value ?? PrinterEncoding.Western, AutoPrint?.Value ?? AutoPrintMode.Ask, baud,
            CodePage?.Value ?? PrinterCodePage.Pc858, Cut?.Value ?? PrinterCutMode.Partial, Qr?.Value ?? PrinterQrMode.Native,
            Handshake?.Value ?? SerialHandshake.None, Model?.Value ?? PrinterModel.Custom);
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
