using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Pos.App.Views;

public partial class GenericItemView : UserControl
{
    public GenericItemView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => AmountBox.Focus());
    }
}
