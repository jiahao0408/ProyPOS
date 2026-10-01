using Avalonia;

namespace Pos.App;

internal static class Program
{
    /// <summary>"--windowed": ventana normal en lugar de pantalla completa (para desarrollar).</summary>
    public static bool Windowed { get; private set; }

    [STAThread]
    public static void Main(string[] args)
    {
        Windowed = args.Contains("--windowed");
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // También lo usa el previsualizador de XAML del IDE.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();
}
