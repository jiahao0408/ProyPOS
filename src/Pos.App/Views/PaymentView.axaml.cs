using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Pos.App.Views;

public partial class PaymentView : UserControl
{
    public PaymentView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => CashBox.Focus());
    }
}
