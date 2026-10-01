using Avalonia;
using Avalonia.Controls;
using Pos.App.ViewModels;

namespace Pos.App.Views;

/// <summary>
/// HW-02: ventana de la pantalla de cliente. Va a pantalla completa en el segundo monitor; si solo hay
/// uno, se abre como ventana normal para poder arrastrarla al visor. Se abre y se cierra según
/// <see cref="CustomerDisplayViewModel.IsEnabled"/> (Ajustes de impresora y cajón).
/// </summary>
public sealed class CustomerDisplayWindow : Window
{
    private CustomerDisplayWindow(CustomerDisplayViewModel display)
    {
        Title = "StarSeaPOS";
        DataContext = display;
        Content = new CustomerDisplayView();
        Width = 1024;
        Height = 600;
        ShowInTaskbar = false;
    }

    /// <summary>Mantiene la ventana abierta mientras la pantalla de cliente esté activada.</summary>
    public static void Follow(CustomerDisplayViewModel display, Window owner)
    {
        CustomerDisplayWindow? window = null;

        void Apply()
        {
            if (display.IsEnabled && window is null)
            {
                window = new CustomerDisplayWindow(display);
                window.Closed += (_, _) => window = null;
                PlaceOnSecondScreen(window, owner);
                window.Show();
            }
            else if (!display.IsEnabled && window is not null)
            {
                window.Close();
            }
        }

        display.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CustomerDisplayViewModel.IsEnabled))
                Apply();
        };
        owner.Opened += (_, _) => Apply();
        owner.Closing += (_, _) => window?.Close();
    }

    private static void PlaceOnSecondScreen(Window window, Window owner)
    {
        var second = owner.Screens.All.FirstOrDefault(s => !s.IsPrimary);
        if (second is null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(second.Bounds.X, second.Bounds.Y);
        window.WindowState = WindowState.FullScreen;
    }
}
