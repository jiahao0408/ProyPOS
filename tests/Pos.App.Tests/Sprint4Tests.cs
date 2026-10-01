using Avalonia.Headless.XUnit;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Core.Printing;
using Pos.Data;
using Pos.Modules.CashRegister;
using Pos.Modules.Products;
using Pos.Modules.Users;

namespace Pos.App.Tests;

/// <summary>Recorridos de la sección 4 (cierre y datos) con la interfaz real.</summary>
public class Sprint4Tests
{
    private sealed class Shop : IDisposable
    {
        public Shop()
        {
            var users = App.Get<UserService>();
            var admin = users.CreateUser("Ana", "1111", Role.Admin).Value!;
            var catalog = App.Get<CatalogService>();
            Vaso = catalog.SaveProduct(null, new ProductInput("Vaso", 2m, 21m, "8410000000016", null, null)).Value!;
            Llavero = catalog.SaveProduct(null, new ProductInput("Llavero", 1.95m, 21m, null, null, null)).Value!;
            App.Get<CashRegisterService>().Open(admin.Id, 100m);

            var login = Assert.IsType<LoginViewModel>(App.Shell.CurrentScreen);
            login.SelectUserCommand.Execute(login.Users.Single(u => u.Name == "Ana"));
            foreach (var d in "1111")
                login.PinEntry.Digit(d.ToString());
            Workspace = Assert.IsType<WorkspaceViewModel>(App.Shell.CurrentScreen);
        }

        public TestApp App { get; } = new();
        public Product Vaso { get; }
        public Product Llavero { get; }
        public WorkspaceViewModel Workspace { get; }

        public T Open<T>() where T : PageViewModel
        {
            Workspace.NavigateCommand.Execute(Workspace.NavItems.Single(n => n.PageType == typeof(T)));
            return Assert.IsType<T>(Workspace.CurrentPage);
        }

        public void Dispose() => App.Dispose();
    }

    [AvaloniaFact]
    public async Task CloseCash_ShowsDifferencePrintsZAndBlocksSales()
    {
        // CAJ-02: introduzco el efectivo contado; muestra el descuadre e imprime el cierre Z.
        using var shop = new Shop();
        var sale = shop.Open<SalePageViewModel>();
        sale.SearchText = "8410000000016";
        sale.SubmitSearchCommand.Execute(null);
        sale.ChargeCommand.Execute(null);
        ((PaymentViewModel)sale.Dialog!).ConfirmCommand.Execute(null); // 2,00 en efectivo

        sale.StartCloseCashCommand.Execute(null);
        var close = Assert.IsType<CloseCashViewModel>(sale.Dialog);
        Assert.Equal("102,00 €", close.ExpectedCashText.Replace(' ', ' '));
        close.CountedText = "101,50";
        Assert.Equal("-0,50 €", close.DifferenceText.Replace(' ', ' '));
        Assert.True(close.IsShort);

        close.CloseCommand.Execute(null);
        await sale.LastPrint;

        Assert.True(sale.IsCashClosed);
        Assert.Contains("Cierre Z nº 1", sale.Message);
        Assert.Equal(1, shop.App.PrintedTickets);
        var z = File.ReadAllBytes(Directory.GetFiles(shop.App.PrintFolder).Single());
        Assert.Contains("CIERRE Z", System.Text.Encoding.Latin1.GetString(z));
    }

    [AvaloniaFact]
    public void CloseCash_RefusedWithTicketInProgress()
    {
        using var shop = new Shop();
        var sale = shop.Open<SalePageViewModel>();
        sale.SearchText = "8410000000016";
        sale.SubmitSearchCommand.Execute(null);

        sale.StartCloseCashCommand.Execute(null);

        Assert.False(sale.IsDialogOpen);
        Assert.Equal("Cobra o vacía el ticket antes de cerrar la caja.", sale.Message);
    }

    [AvaloniaFact]
    public async Task Labels_AssignCodeAndPrint()
    {
        // BAZ-01: etiquetas para productos que llegan sin código.
        using var shop = new Shop();
        var page = shop.Open<LabelsPageViewModel>();

        page.SearchText = "Llavero";
        page.SubmitSearchCommand.Execute(null);
        page.Lines[0].CopiesText = "3";
        page.PrintCommand.Execute(null);
        await page.LastPrint;

        Assert.Equal("3 etiquetas enviadas a la impresora.", page.Message);
        Assert.True(Ean13.IsValid(page.Lines[0].Barcode));
        Assert.Equal(page.Lines[0].Barcode, shop.App.Get<CatalogService>().GetProduct(shop.Llavero.Id)!.Barcode);
        Assert.Equal(1, shop.App.PrintedTickets);
    }

    [AvaloniaFact]
    public void Data_ImportProductsFromCsv()
    {
        // DAT-01: vista previa con errores por fila; importar las válidas.
        using var shop = new Shop();
        var page = shop.Open<DataPageViewModel>();
        var csv = Path.Combine(shop.App.PrintFolder, "..", "productos.csv");
        File.WriteAllText(csv, "Nombre;Precio;IVA;Código\nPlato;4,50;21;8410000000023\n;1;21;\nVaso repetido;2;21;8410000000016\n");

        page.LoadFile(csv);

        Assert.Equal("1 filas válidas, 2 con errores", page.PreviewSummary);
        Assert.Equal(["El nombre es obligatorio.", "Ese código de barras ya lo tiene otro producto."],
            page.PreviewRows.Where(r => !r.IsValid).Select(r => r.Error));
        page.ImportCommand.Execute(null);
        Assert.Equal("1 filas importadas.", page.Message);
        Assert.NotNull(shop.App.Get<CatalogService>().FindByBarcode("8410000000023"));
    }

    [AvaloniaFact]
    public void Data_TemplateAndBackupPasswordChecks()
    {
        using var shop = new Shop();
        var page = shop.Open<DataPageViewModel>();
        var template = Path.Combine(shop.App.PrintFolder, "..", page.SuggestedTemplateName);

        page.SaveTemplate(template);
        Assert.True(File.Exists(template));

        page.BackupPassword = "corta";
        Assert.False(page.ValidateBackupPassword());
        page.BackupPassword = "contraseña-larga";
        page.BackupPasswordConfirm = "otra-distinta";
        Assert.False(page.ValidateBackupPassword());
        Assert.Equal("Las contraseñas no coinciden.", page.Message);
    }

    [AvaloniaFact]
    public void Printer_LabelPrinterCanBeDifferent()
    {
        using var shop = new Shop();
        var page = shop.Open<PrinterPageViewModel>();

        page.LabelsSameAsReceipt = false;
        page.LabelConnection = page.Connections.Single(c => c.Value == PrinterConnection.Network);
        page.LabelTarget = "192.168.1.60";
        page.SaveCommand.Execute(null);

        var profiles = shop.App.Get<ProfileStore>();
        Assert.False(profiles.LabelsUseReceiptPrinter);
        Assert.Equal("192.168.1.60", profiles.GetPrinter(Pos.Core.Hardware.PrinterDestination.Labels).Target);
        Assert.Equal(PrinterConnection.File, profiles.GetPrinter().Connection); // la de tickets no cambia
    }
}
