using Avalonia.Headless.XUnit;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Data;
using Pos.Modules.CashRegister;
using Pos.Modules.Inventory;
using Pos.Modules.Printing;
using Pos.Modules.Products;
using Pos.Modules.Users;

namespace Pos.App.Tests;

/// <summary>Recorridos de la sección 6: descuentos, devoluciones, cambios, ticket regalo y auditoría.</summary>
public class Sprint6Tests
{
    private sealed class Shop : IDisposable
    {
        public Shop(bool asAdmin = false)
        {
            var users = App.Get<UserService>();
            var admin = users.CreateUser("Ana", "1111", Role.Admin).Value!;
            users.CreateUser("Luis", "2222", Role.Cashier);
            var catalog = App.Get<CatalogService>();
            Taza = catalog.SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "8410000000016", null, null)).Value!;
            Plato = catalog.SaveProduct(null, new ProductInput("Plato", 5.00m, 21m, "8410000000023", null, null)).Value!;
            App.Get<CashRegisterService>().Open(admin.Id, 100m);
            var login = Assert.IsType<LoginViewModel>(App.Shell.CurrentScreen);
            login.SelectUserCommand.Execute(login.Users.Single(u => u.Name == (asAdmin ? "Ana" : "Luis")));
            foreach (var d in asAdmin ? "1111" : "2222")
                login.PinEntry.Digit(d.ToString());
            Workspace = Assert.IsType<WorkspaceViewModel>(App.Shell.CurrentScreen);
        }

        public TestApp App { get; } = new();
        public Product Taza { get; }
        public Product Plato { get; }
        public WorkspaceViewModel Workspace { get; }

        public T Open<T>() where T : PageViewModel
        {
            Workspace.NavigateCommand.Execute(Workspace.NavItems.Single(n => n.PageType == typeof(T)));
            if (Workspace.AdminPrompt is { } prompt)
                foreach (var d in "1111")
                    prompt.PinEntry.Digit(d.ToString());
            return Assert.IsType<T>(Workspace.CurrentPage);
        }

        public SalePageViewModel Scan(params string[] codes)
        {
            var sale = Open<SalePageViewModel>();
            foreach (var code in codes)
            {
                sale.SearchText = code;
                sale.SubmitSearchCommand.Execute(null);
            }
            return sale;
        }

        public void Charge(SalePageViewModel sale)
        {
            sale.ChargeCommand.Execute(null);
            ((PaymentViewModel)sale.Dialog!).ConfirmCommand.Execute(null);
        }

        public void Dispose() => App.Dispose();
    }

    private static void TypePin(PinEntryViewModel pad, string pin)
    {
        foreach (var d in pin)
            pad.Digit(d.ToString());
    }

    [AvaloniaFact]
    public void Discount_WithinLimit_ByCashier()
    {
        // VEN-05: descuento por línea dentro del límite (10 % por defecto).
        using var shop = new Shop();
        var sale = shop.Scan("8410000000016", "8410000000016");

        sale.EditLineDiscountCommand.Execute(sale.TicketLines[0]);
        var dialog = Assert.IsType<DiscountViewModel>(sale.Dialog);
        dialog.ValueText = "10";
        dialog.ApplyCommand.Execute(null);

        Assert.False(sale.IsDialogOpen);
        Assert.Equal("6,30 €", sale.TotalText.Replace(' ', ' '));
        Assert.Equal("Dto. -0,70 €", sale.TicketLines[0].DiscountText!.Replace(' ', ' '));
    }

    [AvaloniaFact]
    public void Discount_AboveLimit_AsksForAdminPin_AndUndoesOnCancel()
    {
        using var shop = new Shop();
        var sale = shop.Scan("8410000000016");

        sale.EditTicketDiscountCommand.Execute(null);
        var dialog = Assert.IsType<DiscountViewModel>(sale.Dialog);
        dialog.ValueText = "50";
        dialog.ApplyCommand.Execute(null);

        var prompt = Assert.IsType<AdminPinPromptViewModel>(sale.Dialog);
        prompt.CancelCommand.Execute(null);
        Assert.Equal("3,50 €", sale.TotalText.Replace(' ', ' ')); // se deshace

        sale.EditTicketDiscountCommand.Execute(null);
        ((DiscountViewModel)sale.Dialog!).ValueText = "50";
        ((DiscountViewModel)sale.Dialog!).ApplyCommand.Execute(null);
        TypePin(((AdminPinPromptViewModel)sale.Dialog!).PinEntry, "1111");
        Assert.Equal("1,75 €", sale.TotalText.Replace(' ', ' '));

        shop.Charge(sale);
        var audit = shop.Open<AuditPageViewModel>(); // USR-03
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(("Descuento", "Luis (autorizó Ana)"), (entry.Action, entry.User));
    }

    [AvaloniaFact]
    public async Task Return_ByCashierNeedsAdminPin_AndPrintsRectificative()
    {
        // VEN-06 + FAC-03.
        using var shop = new Shop();
        var profiles = shop.App.Get<ProfileStore>();
        profiles.SavePrinter(profiles.GetPrinter() with { AutoPrint = AutoPrintMode.Yes });
        var sale = shop.Scan("8410000000016", "8410000000016");
        shop.Charge(sale);
        await sale.LastPrint;
        var tickets = shop.Open<TicketsPageViewModel>();
        tickets.Selected = tickets.Invoices.Single();
        Assert.True(tickets.CanReturn);

        tickets.ReturnCommand.Execute(null);
        TypePin(Assert.IsType<AdminPinPromptViewModel>(tickets.Dialog).PinEntry, "1111");
        var dialog = Assert.IsType<ReturnDialogViewModel>(tickets.Dialog);
        dialog.Lines[0].QuantityText = "1";
        dialog.ConfirmCommand.Execute(null);
        Assert.Equal("El motivo es obligatorio.", dialog.Message);

        dialog.Reason = "Taza rota";
        dialog.ConfirmCommand.Execute(null);
        await tickets.LastPrint;

        Assert.False(tickets.IsDialogOpen);
        Assert.Equal("R2026-000001", tickets.Selected!.Code);
        Assert.Contains("FACTURA RECTIFICATIVA", tickets.Preview);
        Assert.Contains("Rectifica a la factura T2026-000001", tickets.Preview);
        Assert.Contains("Motivo de la devolución: Taza rota", tickets.Preview);
        Assert.Contains("-1 x Taza", tickets.Preview);
        Assert.Equal(2, shop.App.PrintedTickets); // el ticket de la venta y la rectificativa
        Assert.Equal(-1, shop.App.Get<StockService>().GetStock(shop.Taza.Id));
    }

    [AvaloniaFact]
    public void Exchange_CustomerPaysDifference()
    {
        // BAZ-07: cambia una taza (3,50) por un plato (5,00).
        using var shop = new Shop();
        shop.Charge(shop.Scan("8410000000016"));
        var tickets = shop.Open<TicketsPageViewModel>();
        tickets.Selected = tickets.Invoices.Single();

        tickets.ExchangeCommand.Execute(null);
        var dialog = Assert.IsType<ExchangeDialogViewModel>(tickets.Dialog);
        dialog.ReturnLines[0].AllCommand.Execute(null);
        dialog.SearchText = "8410000000023";
        dialog.AddItemCommand.Execute(null);
        Assert.Equal("El cliente paga 1,50 €", dialog.DifferenceText.Replace(' ', ' '));
        dialog.ConfirmCommand.Execute(null);

        Assert.False(tickets.IsDialogOpen);
        Assert.StartsWith("Cambio hecho (rectificativa R2026-000001)", tickets.Message);
        Assert.Equal(["T2026-000002", "R2026-000001", "T2026-000001"], tickets.Invoices.Select(i => i.Code)); // más reciente primero
        Assert.Equal(0, shop.App.Get<StockService>().GetStock(shop.Taza.Id));
        Assert.Equal(-1, shop.App.Get<StockService>().GetStock(shop.Plato.Id));
    }

    [AvaloniaFact]
    public void Exchange_RefundByCashierNeedsAdminPin()
    {
        using var shop = new Shop();
        shop.Charge(shop.Scan("8410000000023")); // plato 5,00
        var tickets = shop.Open<TicketsPageViewModel>();
        tickets.Selected = tickets.Invoices.Single();
        tickets.ExchangeCommand.Execute(null);
        var dialog = Assert.IsType<ExchangeDialogViewModel>(tickets.Dialog);
        dialog.ReturnLines[0].AllCommand.Execute(null);
        dialog.SearchText = "8410000000016";        // taza 3,50
        dialog.AddItemCommand.Execute(null);

        dialog.ConfirmCommand.Execute(null);
        Assert.True(dialog.NeedsAdminPin);           // se devuelve dinero
        TypePin(dialog.PinEntry, "1111");

        Assert.False(tickets.IsDialogOpen);
        Assert.Contains("1,50", tickets.Message!.Replace(' ', ' '));
    }

    [AvaloniaFact]
    public void GiftTicket_HasNoPrices()
    {
        using var shop = new Shop();
        shop.Charge(shop.Scan("8410000000016"));
        var doc = shop.App.Get<Pos.Modules.Invoicing.InvoiceService>().Search("1").Single();

        var preview = shop.App.Get<PrintService>().PreviewGift(shop.App.Get<Pos.Modules.Invoicing.InvoiceService>().Get(doc.Id)!);

        Assert.Contains("TICKET REGALO", preview.ToUpperInvariant());
        Assert.Contains("1 x Taza", preview);
        Assert.DoesNotContain("3,50", preview);
        Assert.DoesNotContain("ValidarQR", preview); // el QR de la AEAT lleva el importe
        Assert.Contains("[QR: T2026-000001]", preview);
    }

    [AvaloniaFact]
    public void Rectificative_CannotBeReturnedAgain()
    {
        using var shop = new Shop(asAdmin: true);
        shop.Charge(shop.Scan("8410000000016"));
        var tickets = shop.Open<TicketsPageViewModel>();
        tickets.Selected = tickets.Invoices.Single();
        tickets.ReturnCommand.Execute(null); // admin: sin PIN
        var dialog = Assert.IsType<ReturnDialogViewModel>(tickets.Dialog);
        dialog.Lines[0].AllCommand.Execute(null);
        dialog.Reason = "No le gusta";
        dialog.ConfirmCommand.Execute(null);

        Assert.False(tickets.CanReturn); // seleccionada la rectificativa
        tickets.Selected = tickets.Invoices.Single(i => i.Code == "T2026-000001");
        Assert.False(tickets.CanReturn); // ya devuelto todo
    }
}
