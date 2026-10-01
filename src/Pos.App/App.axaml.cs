using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Pos.App.ViewModels;
using Pos.App.Views;

namespace Pos.App;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        // En los tests de interfaz (headless) no hay escritorio: cada test monta sus propias pantallas.
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = Composition.BuildServices(AppEnvironment.Default());
            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<ShellViewModel>(),
                WindowState = Program.Windowed ? WindowState.Maximized : WindowState.FullScreen,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
