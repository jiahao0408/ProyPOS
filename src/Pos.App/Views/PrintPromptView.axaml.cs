using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Pos.App.Views;

public partial class PrintPromptView : UserControl
{
    public PrintPromptView() => InitializeComponent();

    // Enter = Sí; así el cajero puede seguir sin soltar el teclado.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => YesButton.Focus());
    }
}
