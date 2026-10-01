using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace Pos.App.Views;

/// <summary>El lector escribe en el buscador del verificador mientras está abierto.</summary>
public partial class PriceCheckView : UserControl
{
    public PriceCheckView() => InitializeComponent();

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => SearchBox.Focus());
    }
}
