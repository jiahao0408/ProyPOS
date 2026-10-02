using Avalonia.Headless.XUnit;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Data;
using Pos.Modules.CashRegister;
using Pos.Modules.Hardware;
using Pos.Modules.Products;
using Pos.Modules.Users;

namespace Pos.App.Tests;

/// <summary>v1.1: periféricos desde la interfaz (lector por puerto COM, visor de cliente y ajustes de compatibilidad).</summary>
public class PeripheralUiTests
{
    private sealed class Shop : IDisposable
    {
        public Shop(bool asAdmin = false)
        {
            var users = App.Get<UserService>();
            var admin = users.CreateUser("Ana", "1111", Role.Admin).Value!;
            users.CreateUser("Luis", "2222", Role.Cashier);
            App.Get<CatalogService>().SaveProduct(null, new ProductInput("Taza de cerámica", 3.50m, 21m, "8410000000016", null, null));
            App.Get<CashRegisterService>().Open(admin.Id, 100m);

            var login = Assert.IsType<LoginViewModel>(App.Shell.CurrentScreen);
            login.SelectUserCommand.Execute(login.Users.Single(u => u.Name == (asAdmin ? "Ana" : "Luis")));
            foreach (var d in asAdmin ? "1111" : "2222")
                login.PinEntry.Digit(d.ToString());
            Workspace = Assert.IsType<WorkspaceViewModel>(App.Shell.CurrentScreen);
        }

        public TestApp App { get; } = new();
        public WorkspaceViewModel Workspace { get; }

        public T Open<T>() where T : PageViewModel
        {
            Workspace.NavigateCommand.Execute(Workspace.NavItems.Single(n => n.PageType == typeof(T)));
            return Assert.IsType<T>(Workspace.CurrentPage);
        }

        public void Dispose() => App.Dispose();
    }

    [AvaloniaFact]
    public void SerialScanner_AddsToTheTicket_AndFeedsThePriceChecker()
    {
        using var shop = new Shop();
        var sale = shop.Open<SalePageViewModel>();
        var scanner = shop.App.Get<SerialScanner>();

        scanner.Receive("84100000");
        scanner.Receive("00016\r\n");
        Assert.Equal("Taza de cerámica", Assert.Single(sale.TicketLines).Description);

        sale.CheckPriceCommand.Execute(null);
        scanner.Receive("8410000000016\r");
        var check = Assert.IsType<PriceCheckViewModel>(sale.Dialog);
        Assert.Equal("Taza de cerámica", check.ProductName);
        Assert.Single(sale.TicketLines); // el verificador no añade al ticket
    }

    [AvaloniaFact]
    public void SerialScanner_IsIgnoredOutsideTheSalePage()
    {
        using var shop = new Shop(asAdmin: true);
        shop.Open<ProductsPageViewModel>();

        shop.App.Get<SerialScanner>().Receive("8410000000016\r");

        var sale = shop.Open<SalePageViewModel>();
        Assert.Empty(sale.TicketLines);
    }

    [AvaloniaFact]
    public void PoleDisplay_ShowsLastItemAndTotal()
    {
        using var shop = new Shop();
        var sale = shop.Open<SalePageViewModel>();
        var display = shop.App.Get<CustomerDisplayViewModel>();
        Assert.Equal("Bazar Estrella del M", display.PoleLines.Line1);

        sale.SearchText = "2*8410000000016";
        sale.SubmitSearchCommand.Execute(null);

        Assert.Equal(("Taza de ceramic 7,00", "TOTAL           7,00"), display.PoleLines); // 20 columnas, sin acentos
        Assert.All(new[] { display.PoleLines.Line1, display.PoleLines.Line2 }, l => Assert.Equal(PoleDisplay.Columns, l.Length));
    }

    [AvaloniaFact]
    public void PrinterPage_ModelPresets_AndCustomSettingsAreSaved()
    {
        using var shop = new Shop(asAdmin: true);
        var page = shop.Open<PrinterPageViewModel>();
        Assert.Equal(PrinterModel.EpsonCompatible, page.Model!.Value);

        page.Model = page.Models.Single(m => m.Value == PrinterModel.Generic58NoCutter);
        Assert.Equal((58, PrinterCodePage.Pc437, PrinterCutMode.None, PrinterQrMode.Image),
            (page.PaperWidth!.Value, page.CodePage!.Value, page.Cut!.Value, page.Qr!.Value));

        page.CodePage = page.CodePages.Single(c => c.Value == PrinterCodePage.Wpc1252);
        Assert.Equal(PrinterModel.Custom, page.Model!.Value); // tocar un ajuste lo hace "personalizado"

        page.DrawerPin = page.DrawerPins.Single(p => p.Value == 5);
        page.PoleProtocol = page.PoleProtocols.Single(p => p.Value == PoleDisplayProtocol.Cd5220);
        page.SaveCommand.Execute(null);

        var printer = shop.App.Get<ProfileStore>().GetPrinter();
        Assert.Equal((PrinterModel.Custom, PrinterCodePage.Wpc1252, PrinterCutMode.None, PrinterQrMode.Image),
            (printer.Model, printer.CodePage, printer.Cut, printer.Qr));
        var hardware = shop.App.Get<ProfileStore>().GetHardware();
        Assert.Equal((5, PoleDisplayProtocol.Cd5220), (hardware.DrawerPin, hardware.PoleProtocol));
        Assert.Equal("Lector en modo teclado.", page.ScannerStatus);
    }
}
