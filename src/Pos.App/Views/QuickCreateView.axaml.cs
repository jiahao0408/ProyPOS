using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Pos.App.Views;

public partial class QuickCreateView : UserControl
{
    public QuickCreateView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => NameBox.Focus());
    }
}
