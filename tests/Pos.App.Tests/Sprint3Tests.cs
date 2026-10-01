using Avalonia.Headless.XUnit;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Data;
using Pos.Modules.CashRegister;
using Pos.Modules.Invoicing;
using Pos.Modules.Products;
using Pos.Modules.Users;

namespace Pos.App.Tests;

/// <summary>Recorridos de la sección 3 (imprimir y facturar) con la interfaz real.</summary>
public class Sprint3Tests
{
    private sealed class Shop : IDisposable
    {
        public Shop(AutoPrintMode autoPrint = AutoPrintMode.No, bool asAdmin = true)
        {
            var users = App.Get<UserService>();
            var admin = users.CreateUser("Ana", "1111", Role.Admin).Value!;
            var cashier = users.CreateUser("Luis", "2222", Role.Cashier).Value!;
            App.Get<CatalogService>().SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "8410000000011", null, null));
            App.Get<CashRegisterService>().Open(admin.Id, 100m);
            var profiles = App.Get<ProfileStore>();
            profiles.SavePrinter(profiles.GetPrinter() with { AutoPrint = autoPrint });

            var login = Assert.IsType<LoginViewModel>(App.Shell.CurrentScreen);
            login.SelectUserCommand.Execute(login.Users.Single(u => u.Id == (asAdmin ? admin.Id : cashier.Id)));
            foreach (var d in asAdmin ? "1111" : "2222")
                login.PinEntry.Digit(d.ToString());
            Workspace = Assert.IsType<WorkspaceViewModel>(App.Shell.CurrentScreen);
        }

        public TestApp App { get; } = new();

        public WorkspaceViewModel Workspace { get; }

        public SalePageViewModel Sale => Assert.IsType<SalePageViewModel>(Workspace.CurrentPage);

        public T Open<T>() where T : PageViewModel
        {
            Workspace.NavigateCommand.Execute(Workspace.NavItems.Single(n => n.PageType == typeof(T)));
            return Assert.IsType<T>(Workspace.CurrentPage);
        }

        /// <summary>Vende una taza en efectivo; opcionalmente con factura completa.</summary>
        public PaymentViewModel Sell(bool completeInvoice = false)
        {
            var sale = Sale;
            sale.SearchText = "8410000000011";
            sale.SubmitSearchCommand.Execute(null);
            sale.ChargeCommand.Execute(null);
            var payment = Assert.IsType<PaymentViewModel>(sale.Dialog);
            if (completeInvoice)
            {
                payment.WantsCompleteInvoice = true;
                payment.Customer.Nif = "B12345674";
                payment.Customer.Name = "Papelería Pérez S.L.";
                payment.Customer.Address = "Calle Sol 5";
                payment.Customer.PostalCode = "08001";
                payment.Customer.City = "Barcelona";
            }
            payment.ConfirmCommand.Execute(null);
            return payment;
        }

        public void Dispose() => App.Dispose();
    }

    [AvaloniaFact]
    public async Task AutoPrintYes_PrintsWhenCharging()
    {
        // IMP-01: el ticket se imprime al cobrar.
        using var shop = new Shop(AutoPrintMode.Yes);

        shop.Sell();
        await shop.Sale.LastPrint;

        Assert.Equal(1, shop.App.PrintedTickets);
        Assert.Contains("T2026-000001", shop.Sale.Message);
    }

    [AvaloniaFact]
    public async Task AutoPrintAsk_AsksAndPrintsOnYes()
    {
        using var shop = new Shop(AutoPrintMode.Ask);

        shop.Sell();
        var prompt = Assert.IsType<PrintPromptViewModel>(shop.Sale.Dialog);
        prompt.PrintCommand.Execute(null);
        await shop.Sale.LastPrint;

        Assert.False(shop.Sale.IsDialogOpen);
        Assert.Equal(1, shop.App.PrintedTickets);
    }

    [AvaloniaFact]
    public void AutoPrintAsk_NoDoesNotPrint()
    {
        using var shop = new Shop(AutoPrintMode.Ask);

        shop.Sell();
        Assert.IsType<PrintPromptViewModel>(shop.Sale.Dialog).SkipCommand.Execute(null);

        Assert.Equal(0, shop.App.PrintedTickets);
    }

    [AvaloniaFact]
    public void AutoPrintNo_DoesNotPrint()
    {
        using var shop = new Shop(AutoPrintMode.No);

        shop.Sell();

        Assert.False(shop.Sale.IsDialogOpen);
        Assert.Equal(0, shop.App.PrintedTickets);
    }

    [AvaloniaFact]
    public async Task PrinterFailure_KeepsTheSaleAndWarns()
    {
        using var shop = new Shop(AutoPrintMode.Yes);
        var profiles = shop.App.Get<ProfileStore>();
        profiles.SavePrinter(profiles.GetPrinter() with { Connection = PrinterConnection.Windows, Target = "No existe" });

        shop.Sell();
        await shop.Sale.LastPrint;

        Assert.True(shop.Sale.MessageIsError);
        Assert.StartsWith("No se pudo imprimir", shop.Sale.Message);
        Assert.NotNull(shop.App.Get<InvoiceService>().Search("T2026-000001").SingleOrDefault()); // la venta está guardada
    }

    [AvaloniaFact]
    public void CompleteInvoiceAtCheckout()
    {
        // FAC-02: factura completa con datos del cliente.
        using var shop = new Shop();

        shop.Sell(completeInvoice: true);

        Assert.Contains("F2026-000001", shop.Sale.Message);
    }

    [AvaloniaFact]
    public void CompleteInvoiceAtCheckout_ValidatesNif()
    {
        using var shop = new Shop();
        var sale = shop.Sale;
        sale.SearchText = "8410000000011";
        sale.SubmitSearchCommand.Execute(null);
        sale.ChargeCommand.Execute(null);
        var payment = Assert.IsType<PaymentViewModel>(sale.Dialog);

        payment.WantsCompleteInvoice = true;
        payment.Customer.Nif = "12345678A";
        payment.Customer.Name = "Cliente";
        payment.Customer.Address = "Calle 1";
        payment.ConfirmCommand.Execute(null);

        Assert.True(sale.IsDialogOpen);
        Assert.Equal("El NIF del cliente no es válido.", payment.Message);
    }

    [AvaloniaFact]
    public void KnownCustomer_IsFilledFromNif()
    {
        using var shop = new Shop();
        shop.Sell(completeInvoice: true);

        var sale = shop.Sale;
        sale.SearchText = "8410000000011";
        sale.SubmitSearchCommand.Execute(null);
        sale.ChargeCommand.Execute(null);
        var payment = Assert.IsType<PaymentViewModel>(sale.Dialog);
        payment.Customer.Nif = "b-12345674";

        Assert.Equal("Papelería Pérez S.L.", payment.Customer.Name);
        Assert.Equal("Barcelona", payment.Customer.City);
    }

    [AvaloniaFact]
    public void Checkout_BlockedWithoutBusinessData()
    {
        using var shop = new Shop();
        var profiles = shop.App.Get<ProfileStore>();
        profiles.SaveBusiness(profiles.GetBusiness() with { Nif = "" });

        var payment = shop.Sell();

        Assert.Contains("NIF del negocio", payment.Message);
    }

    [AvaloniaFact]
    public async Task Tickets_ReprintIsMarkedCopy()
    {
        // IMP-02: búsqueda por número; marca COPIA.
        using var shop = new Shop();
        shop.Sell();
        var tickets = shop.Open<TicketsPageViewModel>();

        tickets.SearchText = "1";
        tickets.SearchCommand.Execute(null);
        Assert.Equal("T2026-000001", tickets.Selected!.Code);
        Assert.DoesNotContain("COPIA", tickets.Preview);

        tickets.ReprintCommand.Execute(null);
        await tickets.LastPrint;

        Assert.Equal(1, shop.App.PrintedTickets);
        var bytes = File.ReadAllBytes(Directory.GetFiles(shop.App.PrintFolder).Single());
        Assert.Contains("*** COPIA ***", System.Text.Encoding.Latin1.GetString(bytes));
    }

    [AvaloniaFact]
    public void Tickets_ListsTodayByDefault()
    {
        using var shop = new Shop();
        shop.Sell();
        shop.Sell();

        var tickets = shop.Open<TicketsPageViewModel>();

        Assert.Equal(["T2026-000002", "T2026-000001"], tickets.Invoices.Select(i => i.Code));
    }

    [AvaloniaFact]
    public async Task Tickets_InvoiceATicketOnlyOnce()
    {
        // FAC-06: facturar un ticket ya emitido.
        using var shop = new Shop(asAdmin: false); // lo puede hacer el cajero
        shop.Sell();
        var tickets = shop.Open<TicketsPageViewModel>();
        tickets.Selected = tickets.Invoices.Single();
        Assert.True(tickets.CanInvoice);

        tickets.InvoiceTicketCommand.Execute(null);
        var dialog = Assert.IsType<InvoiceTicketViewModel>(tickets.Dialog);
        dialog.Customer.Nif = "12345678Z";
        dialog.Customer.Name = "Juan García";
        dialog.Customer.Address = "Calle Luna 3";
        dialog.IssueCommand.Execute(null);
        await tickets.LastPrint;

        Assert.False(tickets.IsDialogOpen);
        Assert.Equal("F2026-000001", tickets.Selected!.Code);
        Assert.Contains("Sustituye a la factura simplificada T2026-000001", tickets.Preview);
        Assert.Equal(1, shop.App.PrintedTickets);

        tickets.Selected = tickets.Invoices.Single(i => i.Code == "T2026-000001");
        Assert.False(tickets.CanInvoice); // un ticket solo se puede facturar una vez
        Assert.Equal("Facturado en F2026-000001", tickets.Selected.Status);
    }

    [AvaloniaFact]
    public void Tickets_SavePdf()
    {
        using var shop = new Shop();
        shop.Sell(completeInvoice: true);
        var tickets = shop.Open<TicketsPageViewModel>();
        tickets.Selected = tickets.Invoices.Single();
        var path = Path.Combine(shop.App.PrintFolder, "..", tickets.SuggestedPdfName);

        tickets.SavePdf(path);

        Assert.Equal("F2026-000001.pdf", tickets.SuggestedPdfName);
        Assert.True(new FileInfo(path).Length > 1000);
    }

    [AvaloniaFact]
    public void Business_PreviewUpdatesAndFiscalDataIsRequired()
    {
        // IMP-03: vista previa antes de guardar; datos fiscales obligatorios.
        using var shop = new Shop();
        var page = shop.Open<BusinessPageViewModel>();

        page.Name = "Bazar Nuevo";
        page.FooterMessage = "Cambios en 15 días";
        Assert.Contains("Bazar Nuevo", page.Preview);
        Assert.Contains("Cambios en 15 días", page.Preview);

        page.Nif = "B00000001";
        page.SaveCommand.Execute(null);
        Assert.True(page.MessageIsError);
        Assert.NotEqual("Bazar Nuevo", shop.App.Get<ProfileStore>().GetBusiness().Name); // no se ha guardado

        page.Nif = "B12345674";
        page.SaveCommand.Execute(null);
        Assert.Equal("Bazar Nuevo", shop.App.Get<ProfileStore>().GetBusiness().Name);
    }

    [AvaloniaFact]
    public async Task Printer_TestPrint()
    {
        // HW-01: botón de prueba de impresión.
        using var shop = new Shop();
        var page = shop.Open<PrinterPageViewModel>();
        Assert.Equal(PrinterConnection.File, page.Connection!.Value);

        page.PaperWidth = page.PaperWidths.Single(w => w.Value == 58);
        page.TestCommand.Execute(null);
        await page.LastTest;

        Assert.Equal("Prueba enviada a la impresora.", page.Message);
        Assert.Equal(58, shop.App.Get<ProfileStore>().GetPrinter().PaperWidthMm);
        Assert.Equal(1, shop.App.PrintedTickets);
    }

    [AvaloniaFact]
    public void AllPages_RenderWithoutBindingErrors()
    {
        // Un binding con tipos incompatibles no falla al compilar: Avalonia pinta el error junto al control.
        using var shop = new Shop();
        shop.Sell();
        var window = shop.App.ShowWindow();

        foreach (var nav in shop.Workspace.NavItems)
        {
            shop.Workspace.NavigateCommand.Execute(nav);
            var errors = window.VisibleTexts().Where(t => t.Contains("Exception") || t.Contains("Could not convert")).ToList();
            Assert.True(errors.Count == 0, $"{nav.Title}: {string.Join(" | ", errors)}");
        }
    }

    [AvaloniaFact]
    public void Printer_RequiresTargetForRealPrinters()
    {
        using var shop = new Shop();
        var page = shop.Open<PrinterPageViewModel>();

        page.Connection = page.Connections.Single(c => c.Value == PrinterConnection.Network);
        page.Target = "";
        page.SaveCommand.Execute(null);

        Assert.Equal("Elige la impresora o el puerto.", page.Message);
    }
}
