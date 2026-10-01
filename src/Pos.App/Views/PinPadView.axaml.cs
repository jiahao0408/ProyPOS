using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Pos.App.ViewModels;

namespace Pos.App.Views;

/// <summary>Teclado de PIN que también acepta los números del teclado físico.</summary>
public partial class PinPadView : UserControl
{
    public PinPadView()
    {
        InitializeComponent();
        AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
    }

    private PinEntryViewModel? Entry => DataContext as PinEntryViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Focus();
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        foreach (var c in e.Text ?? "")
            Entry?.Digit(c.ToString());
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Back)
        {
            Entry?.Backspace();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Entry?.Clear();
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
