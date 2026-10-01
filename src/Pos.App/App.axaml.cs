using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Pos.App.ViewModels;
using Pos.App.Views;
using Pos.Modules.DataTransfer;
using Pos.Modules.Verifactu;

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

            // Copia de seguridad diaria automática (requisito de fiabilidad), sin retrasar el arranque.
            var backups = services.GetRequiredService<BackupService>();
            _ = Task.Run(() =>
            {
                try
                {
                    backups.RunDailyBackupIfDue();
                }
                catch (Exception e)
                {
                    Program.LogError("Copia automática", e);
                }
            });
            // VFA-03: cola de envíos a la AEAT en segundo plano.
            var queue = services.GetRequiredService<VerifactuQueue>();
            queue.Failed += e => Program.LogError("Cola Verifactu", e);
            var stop = new CancellationTokenSource();
            _ = Task.Run(() => queue.RunAsync(stop.Token));
            desktop.ShutdownRequested += (_, _) => stop.Cancel();

            desktop.MainWindow = new MainWindow
            {
                DataContext = services.GetRequiredService<ShellViewModel>(),
                WindowState = Program.Windowed ? WindowState.Maximized : WindowState.FullScreen,
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
