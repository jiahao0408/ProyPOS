using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Pos.App.ViewModels;

namespace Pos.App.Views;

/// <summary>
/// El lector de códigos USB escribe como un teclado (HW-04). Para que ningún escaneo se pierda,
/// todo lo que se teclee fuera de un cuadro de texto se redirige al buscador.
/// </summary>
public partial class SalePageView : UserControl
{
    private SalePageViewModel? _subscribed;
    private TopLevel? _topLevel;

    public SalePageView() => InitializeComponent();

    private SalePageViewModel? Vm => DataContext as SalePageViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        if (_subscribed is not null)
            _subscribed.FocusSearchRequested -= FocusSearch;
        _subscribed = Vm;
        if (_subscribed is not null)
            _subscribed.FocusSearchRequested += FocusSearch;
        base.OnDataContextChanged(e);
    }

    // Se escucha en la ventana, no en la página: si nada tiene el foco, el teclado le llega a la ventana.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        _topLevel?.AddHandler(TextInputEvent, OnTextInput, RoutingStrategies.Tunnel);
        _topLevel?.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        FocusSearch();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _topLevel?.RemoveHandler(TextInputEvent, OnTextInput);
        _topLevel?.RemoveHandler(KeyDownEvent, OnKeyDown);
        _topLevel = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void FocusSearch() =>
        Dispatcher.UIThread.Post(() =>
        {
            if (Vm is { IsCashOpen: true, IsDialogOpen: false })
            {
                SearchBox.Focus();
                SearchBox.CaretIndex = SearchBox.Text?.Length ?? 0;
            }
        });

    /// <summary>
    /// v1.1.5: atajos de teclado (configurables en Ajustes). Las teclas que también sirven para escribir
    /// (Enter, Supr…) solo actúan con el buscador vacío: así el lector de códigos sigue funcionando.
    /// </summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (Vm is not { } vm || vm.IsDialogOpen || !IsEffectivelyVisible)
            return;
        // Con la caja cerrada, solo el atajo de abrirla.
        var searchIsEmpty = string.IsNullOrWhiteSpace(vm.IsCashOpen ? SearchBox.Text : null);
        if (vm.Shortcuts.Match(e, searchIsEmpty) is { } action
            && (vm.IsCashOpen || action == ShortcutAction.OpenCashOrDrawer)
            && vm.ExecuteShortcut(action))
        {
            e.Handled = true;
        }
    }

    private void OnTextInput(object? sender, TextInputEventArgs e)
    {
        // v1.1.5: atajos de un carácter ("+" = cobrar con tarjeta), solo con el buscador vacío.
        if (Vm is { IsCashOpen: true, IsDialogOpen: false } shortcutVm && IsEffectivelyVisible
            && shortcutVm.Shortcuts.MatchCharacter(e.Text, string.IsNullOrWhiteSpace(SearchBox.Text)) is { } action
            && shortcutVm.ExecuteShortcut(action))
        {
            e.Handled = true;
            return;
        }

        if (Vm is not { IsCashOpen: true, IsDialogOpen: false } || e.Source is TextBox || string.IsNullOrEmpty(e.Text))
            return;

        SearchBox.Text += e.Text;
        SearchBox.Focus();
        SearchBox.CaretIndex = SearchBox.Text.Length;
        e.Handled = true;
    }
}
