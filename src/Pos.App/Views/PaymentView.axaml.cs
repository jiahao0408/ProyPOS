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
        // Con tarjeta (atajo "+") no hay importe que teclear: Enter confirma el cobro.
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is Pos.App.ViewModels.PaymentViewModel { IsCard: true })
                ConfirmButton.Focus();
            else
                CashBox.Focus();
        });
    }
}
