using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Localization;
using Pos.Core.Security;
using Pos.Modules.DataTransfer;

namespace Pos.App.ViewModels;

public sealed record ImportRowView(string Line, string Values, string? Error, bool IsValid);

/// <summary>Qué se exporta: tablas (DAT-02), facturas para la gestoría en Excel o PDF (FAC-05).</summary>
public enum ExportChoice
{
    Products,
    Customers,
    Sales,
    Invoices,
    InvoiceBookPdf,
}

/// <summary>
/// DAT-01: importar productos y clientes. DAT-03: copia completa y restauración.
/// DAT-02 y FAC-05: exportar. DAT-04: exportar y verificar el registro de facturación.
/// </summary>
public partial class DataPageViewModel(
    ILocalizer localizer,
    ImportService importer,
    BackupService backups,
    ExportService exports,
    InvoiceBookPdf invoiceBook,
    BillingRecordExport billing,
    TimeProvider clock,
    ISession session) : PageViewModel(localizer)
{
    private ImportPreview? _preview;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImportKind))]
    private Choice<ImportKind>? _kind;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanImport))]
    private string _previewSummary = "";

    [ObservableProperty]
    private string _backupPassword = "";

    [ObservableProperty]
    private string _backupPasswordConfirm = "";

    [ObservableProperty]
    private string _restorePassword = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore))]
    private string? _restoreFile;

    /// <summary>Restaurar pide confirmación: la primera pulsación solo avisa.</summary>
    [ObservableProperty]
    private bool _isRestorePending;

    public IReadOnlyList<Choice<ImportKind>> Kinds { get; private set; } = [];

    // --- Exportar (DAT-02, FAC-05) ---

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExportNeedsDates), nameof(ExportIsPdf))]
    private Choice<ExportChoice>? _exportKind;

    [ObservableProperty]
    private DateTime? _exportFrom;

    [ObservableProperty]
    private DateTime? _exportTo;

    public IReadOnlyList<Choice<ExportChoice>> ExportKinds { get; private set; } = [];

    public bool ExportNeedsDates => ExportKind?.Value is ExportChoice.Sales or ExportChoice.Invoices or ExportChoice.InvoiceBookPdf;

    public bool ExportIsPdf => ExportKind?.Value == ExportChoice.InvoiceBookPdf;

    public string SuggestedExportName
    {
        get
        {
            var name = ExportKind?.Title ?? "export";
            var period = ExportNeedsDates ? $"-{ExportFrom:yyyyMMdd}-{ExportTo:yyyyMMdd}" : "";
            return $"{name}{period}.{(ExportIsPdf ? "pdf" : "xlsx")}";
        }
    }

    public string SuggestedBillingName => billing.SuggestedFileName;

    public ImportKind ImportKind => Kind?.Value ?? ImportKind.Products;

    public ObservableCollection<ImportRowView> PreviewRows { get; } = [];

    public bool CanImport => _preview is { ValidCount: > 0 };

    public bool BackupsAvailable => backups.IsAvailable;

    public bool CanRestore => RestoreFile is not null;

    public string RestoreFileName => RestoreFile is null ? "" : Path.GetFileName(RestoreFile);

    public string SuggestedTemplateName => $"{L[ImportKind == ImportKind.Products ? "TemplateProducts" : "TemplateCustomers"]}.xlsx";

    public string SuggestedBackupName => $"StarSeaPOS-{DateTime.Now:yyyyMMdd-HHmm}.sspos";

    public override void Load()
    {
        Kinds = [new(ImportKind.Products, L["NavProducts"]), new(ImportKind.Customers, L["Customers"])];
        OnPropertyChanged(nameof(Kinds));
        Kind = Kinds[0];

        ExportKinds =
        [
            new(ExportChoice.Products, L["NavProducts"]),
            new(ExportChoice.Customers, L["Customers"]),
            new(ExportChoice.Sales, L["ExportSales"]),
            new(ExportChoice.Invoices, L["ExportInvoices"]),
            new(ExportChoice.InvoiceBookPdf, L["ExportInvoiceBookPdf"]),
        ];
        OnPropertyChanged(nameof(ExportKinds));
        ExportKind = ExportKinds[0];
        var today = clock.GetLocalNow().Date;
        ExportFrom = new DateTime(today.Year, today.Month, 1);
        ExportTo = today;
    }

    // --- DAT-02 / FAC-05 ---

    public void Export(string path)
    {
        if (ExportKind is not { } kind)
            return;
        var from = DateOnly.FromDateTime(ExportFrom ?? DateTime.Today);
        var to = DateOnly.FromDateTime(ExportTo ?? DateTime.Today);
        try
        {
            var count = kind.Value switch
            {
                ExportChoice.InvoiceBookPdf => invoiceBook.Save(path, from, to),
                ExportChoice.Products => exports.Export(Pos.Modules.DataTransfer.ExportKind.Products, path, from, to),
                ExportChoice.Customers => exports.Export(Pos.Modules.DataTransfer.ExportKind.Customers, path, from, to),
                ExportChoice.Sales => exports.Export(Pos.Modules.DataTransfer.ExportKind.Sales, path, from, to),
                _ => exports.Export(Pos.Modules.DataTransfer.ExportKind.Invoices, path, from, to),
            };
            Message = string.Format(L["ExportDone"], Path.GetFileName(path), count);
            MessageIsError = false;
        }
        catch (IOException e)
        {
            Message = string.Format(L["ErrorFileWrite"], e.Message);
            MessageIsError = true;
        }
    }

    // --- DAT-04 ---

    public void ExportBillingRecord(string path)
    {
        try
        {
            var result = billing.Export(path);
            Message = string.Format(L["BillingExported"], Path.GetFileName(path), result.Records, result.FinalHash ?? "—");
            MessageIsError = false;
        }
        catch (IOException e)
        {
            Message = string.Format(L["ErrorFileWrite"], e.Message);
            MessageIsError = true;
        }
    }

    public void VerifyBillingRecord(string path)
    {
        var result = billing.Verify(path);
        if (result.IsValid)
        {
            Message = string.Format(L["BillingVerified"], result.Records, result.FinalHash ?? "—");
            MessageIsError = false;
        }
        else
        {
            Message = result.Detail is null ? L[result.ErrorKey!] : $"{L[result.ErrorKey!]} ({result.Detail})";
            MessageIsError = true;
        }
    }

    partial void OnKindChanged(Choice<ImportKind>? value) => ClearPreview();

    partial void OnRestoreFileChanged(string? value)
    {
        IsRestorePending = false;
        OnPropertyChanged(nameof(RestoreFileName));
    }

    // --- DAT-01 ---

    public void SaveTemplate(string path)
    {
        importer.WriteTemplate(ImportKind, path);
        Message = string.Format(L["FileSaved"], Path.GetFileName(path));
        MessageIsError = false;
    }

    public void LoadFile(string path)
    {
        try
        {
            _preview = importer.Preview(ImportKind, path);
        }
        catch (Exception e) when (e is IOException or InvalidDataException or FormatException or ArgumentException)
        {
            ClearPreview();
            Message = string.Format(L["ErrorFileRead"], e.Message);
            MessageIsError = true;
            return;
        }

        PreviewRows.Clear();
        foreach (var row in _preview.Rows)
            PreviewRows.Add(new ImportRowView(row.Line.ToString(L.Culture), string.Join(" · ", row.Values.Where(v => v.Length > 0)),
                row.ErrorKey is null ? null : L[row.ErrorKey], row.IsValid));
        PreviewSummary = string.Format(L["ImportPreviewSummary"], _preview.ValidCount, _preview.ErrorCount);
        ClearMessage();
    }

    [RelayCommand]
    private void Import()
    {
        if (_preview is null)
            return;
        var count = importer.Import(_preview);
        ClearPreview();
        Message = string.Format(L["ImportDone"], count);
        MessageIsError = false;
    }

    // --- DAT-03 ---

    /// <summary>Comprueba la contraseña antes de abrir el diálogo "Guardar como".</summary>
    public bool ValidateBackupPassword()
    {
        if (BackupPassword.Length < BackupService.MinPasswordLength)
        {
            ShowError("ErrorBackupPasswordShort");
            return false;
        }
        if (BackupPassword != BackupPasswordConfirm)
        {
            ShowError("ErrorPasswordMismatch");
            return false;
        }
        return true;
    }

    public void CreateBackup(string path)
    {
        if (!ValidateBackupPassword() || !Check(backups.CreateBackup(path, BackupPassword)))
            return;
        BackupPassword = BackupPasswordConfirm = "";
        Message = string.Format(L["BackupCreated"], Path.GetFileName(path));
        MessageIsError = false;
    }

    [RelayCommand]
    private void Restore()
    {
        if (RestoreFile is null)
            return;
        if (!IsRestorePending)
        {
            IsRestorePending = true;
            ShowError("ConfirmRestore");
            return;
        }

        IsRestorePending = false;
        if (!Check(backups.Restore(RestoreFile, RestorePassword)))
            return;

        // Todo ha cambiado (usuarios incluidos): se cierra la sesión para empezar de nuevo.
        RestorePassword = "";
        RestoreFile = null;
        session.SignOut();
    }

    private void ClearPreview()
    {
        _preview = null;
        PreviewRows.Clear();
        PreviewSummary = "";
        OnPropertyChanged(nameof(CanImport));
    }
}
