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
/// STARSEAPOS_SCREENSHOTS indica una carpeta; si no, el test no hace nada.
/// </summary>
public class ScreenshotTests
{
    private static readonly string? Folder = Environment.GetEnvironmentVariable("STARSEAPOS_SCREENSHOTS");

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

        // Sección 2: ticket, cobro y alta rápida
        foreach (var code in new[] { "8410000000011", "8410000000011", "8410000000028", "4*8410000000035" })
        {
            sale.SearchText = code;
            sale.SubmitSearchCommand.Execute(null);
        }
        sale.AddGenericCommand.Execute(sale.GenericSections[0]);
        ((GenericItemViewModel)sale.Dialog!).AmountText = "4,95";
        Capture(window, "10-generico");
        ((GenericItemViewModel)sale.Dialog!).AddCommand.Execute(null);
        Capture(window, "11-ticket");

        sale.ChargeCommand.Execute(null);
        ((PaymentViewModel)sale.Dialog!).CashTenderedText = "50";
        Capture(window, "12-cobro-efectivo");
        ((PaymentViewModel)sale.Dialog!).SetModeCommand.Execute(PaymentMode.Mixed);
        ((PaymentViewModel)sale.Dialog!).CardAmountText = "10";
        ((PaymentViewModel)sale.Dialog!).CashTenderedText = "20";
        Capture(window, "13-cobro-mixto");
        ((PaymentViewModel)sale.Dialog!).ConfirmCommand.Execute(null);
        Capture(window, "14-venta-cobrada");

        sale.SearchText = "8419999999999";
        sale.SubmitSearchCommand.Execute(null);
        ((QuickCreateViewModel)sale.Dialog!).Name = "Pelota de playa";
        ((QuickCreateViewModel)sale.Dialog!).PriceText = "2,50";
        Capture(window, "15-alta-rapida");
        ((QuickCreateViewModel)sale.Dialog!).CancelCommand.Execute(null);

        // Sección 3: factura completa al cobrar, tickets, negocio e impresora
        sale.SearchText = "8410000000011";
        sale.SubmitSearchCommand.Execute(null);
        sale.ChargeCommand.Execute(null);
        var pay = (PaymentViewModel)sale.Dialog!;
        pay.WantsCompleteInvoice = true;
        pay.Customer.Nif = "B12345674";
        pay.Customer.Name = "Papelería Pérez S.L.";
        pay.Customer.Address = "Calle Sol 5";
        pay.Customer.PostalCode = "08001";
        pay.Customer.City = "Barcelona";
        Capture(window, "20-cobro-factura-completa");
        pay.ConfirmCommand.Execute(null);

        workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == typeof(TicketsPageViewModel)));
        var tickets = (TicketsPageViewModel)workspace.CurrentPage!;
        tickets.Selected = tickets.Invoices.Single(i => i.Code == "T2026-000001");
        Capture(window, "21-tickets");
        tickets.InvoiceTicketCommand.Execute(null);
        var invoiceDialog = (InvoiceTicketViewModel)tickets.Dialog!;
        invoiceDialog.Customer.Nif = "12345678Z";
        invoiceDialog.Customer.Name = "Juan García López";
        invoiceDialog.Customer.Address = "Calle Luna 3, 2º B";
        invoiceDialog.Customer.PostalCode = "28005";
        invoiceDialog.Customer.City = "Madrid";
        Capture(window, "22-facturar-ticket");
        invoiceDialog.IssueCommand.Execute(null);
        Capture(window, "23-factura-emitida");

        workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == typeof(BusinessPageViewModel)));
        foreach (var d in "1111")
            workspace.AdminPrompt!.PinEntry.Digit(d.ToString());
        Capture(window, "24-negocio");
        workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == typeof(PrinterPageViewModel)));
        foreach (var d in "1111")
            workspace.AdminPrompt!.PinEntry.Digit(d.ToString());
        Capture(window, "25-impresora");

        // Sección 4: entradas, etiquetas, datos y cierre de caja
        void OpenAdmin(Type page)
        {
            workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == page));
            if (workspace.AdminPrompt is { } prompt)
                foreach (var d in "1111")
                    prompt.PinEntry.Digit(d.ToString());
        }

        OpenAdmin(typeof(ReceiptsPageViewModel));
        var receiptsPage = (ReceiptsPageViewModel)workspace.CurrentPage!;
        receiptsPage.NewSupplierName = "Mayorista Oriente";
        receiptsPage.AddSupplierCommand.Execute(null);
        receiptsPage.Reference = "ALB-2026-0815";
        foreach (var code in new[] { "8410000000035", "8410000000011" })
        {
            receiptsPage.SearchText = code;
            receiptsPage.SubmitSearchCommand.Execute(null);
        }
        receiptsPage.Lines[0].QuantityText = "3";
        receiptsPage.Lines[0].CostText = "12";
        receiptsPage.Lines[1].QuantityText = "24";
        receiptsPage.Lines[1].CostText = "1,10";
        Capture(window, "30-entrada");

        OpenAdmin(typeof(LabelsPageViewModel));
        var labelsPage = (LabelsPageViewModel)workspace.CurrentPage!;
        labelsPage.SearchText = "Cuaderno";
        labelsPage.SubmitSearchCommand.Execute(null);
        labelsPage.SearchText = "Taza";
        labelsPage.SubmitSearchCommand.Execute(null);
        labelsPage.Lines[1].CopiesText = "12";
        Capture(window, "31-etiquetas");

        OpenAdmin(typeof(DataPageViewModel));
        var dataPage = (DataPageViewModel)workspace.CurrentPage!;
        var csv = Path.Combine(Folder!, "import.csv");
        File.WriteAllText(csv, "Nombre;Precio;IVA;Código;Categoría\nPlato hondo;4,50;21;8410000000023;Hogar\n;1;21;;\nGoma;0,40;7;;Papelería\nLibro de cuentos;10,40;4;;Papelería\n");
        dataPage.LoadFile(csv);
        Capture(window, "32-datos");

        workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == typeof(SalePageViewModel)));
        var salePage = (SalePageViewModel)workspace.CurrentPage!;
        salePage.StartCloseCashCommand.Execute(null);
        ((CloseCashViewModel)salePage.Dialog!).CountedText = "170";
        Capture(window, "33-cierre-caja");
        ((CloseCashViewModel)salePage.Dialog!).CancelCommand.Execute(null);

        // Sección 5: Verifactu y Acerca de
        OpenAdmin(typeof(VerifactuPageViewModel));
        Capture(window, "40-verifactu");
        workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == typeof(AboutPageViewModel)));
        Capture(window, "41-acerca-de");

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
