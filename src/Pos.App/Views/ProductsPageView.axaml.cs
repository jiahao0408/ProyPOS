using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pos.App.ViewModels;

namespace Pos.App.Views;

public partial class ProductsPageView : UserControl
{
    private static readonly FilePickerFileType Images = new("Images")
    {
        Patterns = ["*.png", "*.jpg", "*.jpeg", "*.bmp", "*.webp"],
    };

    public ProductsPageView() => InitializeComponent();

    private async void OnChoosePhoto(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ProductsPageViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [Images],
        });

        if (files.Count > 0 && files[0].TryGetLocalPath() is { } path)
            vm.SetPhotoFromFile(path);
    }
}
