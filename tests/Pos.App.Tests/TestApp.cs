using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Pos.App;
using Pos.App.ViewModels;
using Pos.App.Views;
using Pos.Data;

[assembly: AvaloniaTestApplication(typeof(Pos.App.Tests.TestAppBuilder))]

namespace Pos.App.Tests;

public static class TestAppBuilder
{
    // Skia real (no el dibujo simulado) para poder sacar capturas de las pantallas.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// La app completa (servicios reales + vistas reales) sobre una BD en memoria
/// y un reloj falso. Se usa dentro de un [AvaloniaFact].
/// </summary>
public sealed class TestApp : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string _dataDirectory;

    public TestApp()
    {
        SQLitePCL.Batteries_V2.Init();
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var factory = new PosDbContextFactory(new DbContextOptionsBuilder<PosDbContext>().UseSqlite(_connection).Options);
        using (var db = factory.CreateDbContext())
            db.Database.Migrate();

        _dataDirectory = Directory.CreateTempSubdirectory("proypos-test-").FullName;
        Services = Composition.BuildServices(new AppEnvironment(
            factory, _dataDirectory, Path.Combine(AppContext.BaseDirectory, "locales"), Clock));
    }

    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));

    public IServiceProvider Services { get; }

    public ShellViewModel Shell => Services.GetRequiredService<ShellViewModel>();

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <summary>Abre la ventana principal (sin pantalla) para comprobar lo que se ve.</summary>
    public MainWindow ShowWindow()
    {
        var window = new MainWindow { DataContext = Shell, Width = 1366, Height = 768 };
        window.Show();
        return window;
    }

    public void Dispose()
    {
        _connection.Dispose();
        try
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
        catch (IOException)
        {
            // una foto aún abierta no debe hacer fallar el test
        }
    }
}

public static class VisualExtensions
{
    /// <summary>Todos los textos visibles de la ventana, para comprobar qué se muestra.</summary>
    public static IReadOnlyList<string> VisibleTexts(this Window window)
    {
        // Deja que la ventana procese los cambios pendientes (pantalla nueva, textos nuevos).
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        // Un TextBlock con <Run> guarda el texto en Inlines; cada Run cuenta como un texto.
        return window.GetVisualDescendants()
            .OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible)
            .SelectMany(t => t.Inlines is { Count: > 0 } inlines
                ? inlines.OfType<Run>().Select(r => r.Text)
                : [t.Text])
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Select(text => text!)
            .ToList();
    }
}
