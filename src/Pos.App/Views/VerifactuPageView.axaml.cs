using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pos.App.ViewModels;

namespace Pos.App.Views;

public partial class VerifactuPageView : UserControl
{
    private static readonly FilePickerFileType Certificates = new("PFX / P12") { Patterns = ["*.pfx", "*.p12"] };

    public VerifactuPageView() => InitializeComponent();

    private async void OnLoadCertificate(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not VerifactuPageViewModel vm || TopLevel.GetTopLevel(this)?.StorageProvider is not { } storage)
            return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { FileTypeFilter = [Certificates] });
        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            vm.LoadCertificate(await File.ReadAllBytesAsync(path));
    }
}
