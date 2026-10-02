using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Pos.App.Views;

public partial class ShortcutHelpView : UserControl
{
    public ShortcutHelpView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => CloseButton.Focus());
    }
}
