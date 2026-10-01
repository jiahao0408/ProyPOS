using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Modules.Products;
using Pos.Modules.Users;

namespace Pos.App.Tests;

/// <summary>
/// Capturas de las pantallas para revisarlas a ojo. Solo se generan si la variable
/// PROYPOS_SCREENSHOTS indica una carpeta; si no, el test no hace nada.
/// </summary>
public class ScreenshotTests
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("PROYPOS_SCREENSHOTS");

    private static void Capture(Window window, string name)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        window.CaptureRenderedFrame()?.Save(Path.Combine(Folder!, name + ".png"));
    }

    [AvaloniaFact]
    public void CaptureSection1Screens()
    {
        if (string.IsNullOrEmpty(Folder))
            return;
        Directory.CreateDirectory(Folder);

        using (var empty = new TestApp())
            Capture(empty.ShowWindow(), "01-primer-arranque");

        using var app = new TestApp();
        var users = app.Get<UserService>();
        users.CreateFirstAdmin("Ana", "1111");
        users.CreateUser("Luis", "2222", Role.Cashier);
        users.CreateUser("Mei", "3333", Role.Cashier);
        var catalog = app.Get<CatalogService>();
        var hogar = catalog.SaveCategory(null, "Hogar", 0, "#F59E0B", true).Value!;
        var papeleria = catalog.SaveCategory(null, "Papelería", 1, "#10B981", true).Value!;
        catalog.SaveCategory(null, "Juguetes", 2, "#6366F1", false);
        catalog.SaveProduct(null, new ProductInput("Taza de cerámica", 3.50m, 21m, "8410000000011", hogar.Id, null));
        catalog.SaveProduct(null, new ProductInput("Cuaderno A4", 2.95m, 21m, "8410000000028", papeleria.Id, null));
        catalog.SaveProduct(null, new ProductInput("Bolígrafo azul", 0.60m, 21m, "8410000000035", papeleria.Id, null, 50));

        var window = app.ShowWindow();
        var loginVm = Assert.IsType<LoginViewModel>(app.Shell.CurrentScreen);
        Capture(window, "02-login");

        loginVm.SelectUserCommand.Execute(loginVm.Users.Single(u => u.Name == "Luis"));
        loginVm.PinEntry.Digit("2");
        Capture(window, "03-pin");

        loginVm.PinEntry.Digit("2");
        loginVm.PinEntry.Digit("2");
        loginVm.PinEntry.Digit("2");
        var workspace = Assert.IsType<WorkspaceViewModel>(app.Shell.CurrentScreen);
        Capture(window, "04-caja-cerrada");

        var sale = Assert.IsType<SalePageViewModel>(workspace.CurrentPage);
        sale.OpeningFloatText = "150";
        sale.OpenCashCommand.Execute(null);
        Capture(window, "05-venta");

        workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == typeof(ProductsPageViewModel)));
        Capture(window, "06-pin-admin");

        foreach (var d in "1111")
            workspace.AdminPrompt!.PinEntry.Digit(d.ToString());
        var products = Assert.IsType<ProductsPageViewModel>(workspace.CurrentPage);
        products.SelectedRow = products.Products.First();
        Capture(window, "07-productos");

        workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == typeof(SettingsPageViewModel)));
        foreach (var d in "1111")
            workspace.AdminPrompt!.PinEntry.Digit(d.ToString());
        var settings = Assert.IsType<SettingsPageViewModel>(workspace.CurrentPage);
        settings.SelectedLanguage = settings.Languages.Single(l => l.Code == "zh");
        settings.SaveCommand.Execute(null);
        Capture(window, "08-ajustes-chino");
    }
}
