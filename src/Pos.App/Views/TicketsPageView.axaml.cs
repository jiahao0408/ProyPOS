using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Pos.App.ViewModels;

namespace Pos.App.Views;

public partial class TicketsPageView : UserControl
{
    public TicketsPageView() => InitializeComponent();

    private async void OnSavePdf(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not TicketsPageViewModel vm || TopLevel.GetTopLevel(this) is not { } topLevel)
            return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = vm.SuggestedPdfName,
            DefaultExtension = "pdf",
            FileTypeChoices = [new FilePickerFileType("PDF") { Patterns = ["*.pdf"] }],
        });

        if (file?.TryGetLocalPath() is { } path)
            vm.SavePdf(path);
    }
}
