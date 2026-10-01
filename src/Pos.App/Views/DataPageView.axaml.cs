using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pos.App.ViewModels;

namespace Pos.App.Views;

/// <summary>Los diálogos de abrir y guardar ficheros van aquí; el resto, en el ViewModel.</summary>
public partial class DataPageView : UserControl
{
    private static readonly FilePickerFileType Tables = new("CSV / Excel") { Patterns = ["*.csv", "*.xlsx"] };
    private static readonly FilePickerFileType Excel = new("Excel") { Patterns = ["*.xlsx"] };
    private static readonly FilePickerFileType Backup = new("StarSeaPOS") { Patterns = ["*.sspos"] };
    private static readonly FilePickerFileType Csv = new("CSV") { Patterns = ["*.csv"] };
    private static readonly FilePickerFileType Pdf = new("PDF") { Patterns = ["*.pdf"] };
    private static readonly FilePickerFileType Zip = new("ZIP") { Patterns = ["*.zip"] };

    public DataPageView() => InitializeComponent();

    private DataPageViewModel? Vm => DataContext as DataPageViewModel;

    private IStorageProvider? Storage => TopLevel.GetTopLevel(this)?.StorageProvider;

    private async void OnSaveTemplate(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || Storage is not { } storage)
            return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = vm.SuggestedTemplateName,
            DefaultExtension = "xlsx",
            FileTypeChoices = [Excel],
        });
        if (file?.TryGetLocalPath() is { } path)
            vm.SaveTemplate(path);
    }

    private async void OnChooseImportFile(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || Storage is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [Tables] });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            vm.LoadFile(path);
    }

    private async void OnCreateBackup(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || Storage is not { } storage || !vm.ValidateBackupPassword())
            return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = vm.SuggestedBackupName,
            DefaultExtension = "sspos",
            FileTypeChoices = [Backup],
        });
        if (file?.TryGetLocalPath() is { } path)
            vm.CreateBackup(path);
    }

    private async void OnExport(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || Storage is not { } storage)
            return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = vm.SuggestedExportName,
            DefaultExtension = vm.ExportIsPdf ? "pdf" : "xlsx",
            FileTypeChoices = vm.ExportIsPdf ? [Pdf] : [Excel, Csv],
        });
        if (file?.TryGetLocalPath() is { } path)
            vm.Export(path);
    }

    private async void OnExportBilling(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || Storage is not { } storage)
            return;
        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = vm.SuggestedBillingName,
            DefaultExtension = "zip",
            FileTypeChoices = [Zip],
        });
        if (file?.TryGetLocalPath() is { } path)
            vm.ExportBillingRecord(path);
    }

    private async void OnVerifyBilling(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || Storage is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [Zip] });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            vm.VerifyBillingRecord(path);
    }

    private async void OnChooseRestoreFile(object? sender, RoutedEventArgs e)
    {
        if (Vm is not { } vm || Storage is not { } storage)
            return;
        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [Backup] });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            vm.RestoreFile = path;
    }
}
