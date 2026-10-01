using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pos.App.ViewModels;

namespace Pos.App.Views;

public partial class BusinessPageView : UserControl
{
    private static readonly FilePickerFileType Images = new("Images")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp"],
    };

    public BusinessPageView() => InitializeComponent();

    private async void OnChooseLogo(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BusinessPageViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [Images],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            vm.SetLogoFromFile(path);
    }
}
