using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Pos.App.ViewModels;

namespace Pos.App.Views;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _inactivityTimer = new() { Interval = TimeSpan.FromSeconds(15) };

    public MainWindow()
    {
        InitializeComponent();

        // Cualquier toque o tecla cuenta como actividad (USR-01: cierre de sesión por inactividad).
        AddHandler(PointerPressedEvent, (_, _) => Shell?.RegisterActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
        AddHandler(KeyDownEvent, (_, _) => Shell?.RegisterActivity(), RoutingStrategies.Tunnel, handledEventsToo: true);
        _inactivityTimer.Tick += (_, _) => Shell?.CheckInactivity();
        _inactivityTimer.Start();
    }

    private ShellViewModel? Shell => DataContext as ShellViewModel;

    protected override void OnClosed(EventArgs e)
    {
        _inactivityTimer.Stop();
        base.OnClosed(e);
    }
}
