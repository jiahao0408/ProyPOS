using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Localization;
using Pos.Core.Security;
using Pos.Modules.DataTransfer;

namespace Pos.App.ViewModels;

public sealed record ImportRowView(string Line, string Values, string? Error, bool IsValid);

/// <summary>DAT-01: importar productos y clientes. DAT-03: copia completa y restauración.</summary>
public partial class DataPageViewModel(
    ILocalizer localizer,
    ImportService importer,
    BackupService backups,
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
