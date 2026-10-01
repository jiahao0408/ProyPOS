using Avalonia;
using Pos.Data;

namespace Pos.App;

internal static class Program
{
    /// <summary>"--windowed": ventana normal en lugar de pantalla completa (para desarrollar).</summary>
    public static bool Windowed { get; private set; }

    /// <summary>Carpeta de datos: la estándar o la de STARSEAPOS_DATA_DIR.</summary>
    public static string DataDirectory { get; } =
        Environment.GetEnvironmentVariable("STARSEAPOS_DATA_DIR") is { Length: > 0 } custom ? custom : PosDatabase.DefaultDataDirectory;

    [STAThread]
    public static int Main(string[] args)
    {
        Windowed = args.Contains("--windowed");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogError("Excepción no controlada", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => LogError("Tarea en segundo plano", e.Exception);

        try
        {
            return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception e)
        {
            LogError("Error al arrancar", e);
            throw;
        }
    }

    // También lo usa el previsualizador de XAML del IDE.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

    /// <summary>Registro de errores en %LOCALAPPDATA%\StarSeaPOS\logs\errores.log, para poder diagnosticar fallos en la tienda.</summary>
    public static void LogError(string context, Exception? exception)
    {
        try
        {
            var folder = Path.Combine(DataDirectory, "logs");
            Directory.CreateDirectory(folder);
            File.AppendAllText(Path.Combine(folder, "errores.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {context}: {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (IOException)
        {
            // si ni siquiera se puede escribir el registro, no hay nada más que hacer
        }
    }
}
