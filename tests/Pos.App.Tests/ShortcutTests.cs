using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Data;
using Pos.Modules.CashRegister;
using Pos.Modules.Products;
using Pos.Modules.Users;

namespace Pos.App.Tests;

/// <summary>v1.1.5: atajos de teclado de la venta, sintaxis del buscador y modo táctil / teclado.</summary>
public class ShortcutTests
{
    private sealed class Shop : IDisposable
    {
        public Shop(bool openCash = true, bool asAdmin = false)
        {
            var users = App.Get<UserService>();
            var admin = users.CreateUser("Ana", "1111", Role.Admin).Value!;
            users.CreateUser("Luis", "2222", Role.Cashier);
            App.Get<CatalogService>().SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "8410000000016", null, null));
            if (openCash)
                App.Get<CashRegisterService>().Open(admin.Id, 100m);

            var login = Assert.IsType<LoginViewModel>(App.Shell.CurrentScreen);
            login.SelectUserCommand.Execute(login.Users.Single(u => u.Name == (asAdmin ? "Ana" : "Luis")));
            foreach (var d in asAdmin ? "1111" : "2222")
                login.PinEntry.Digit(d.ToString());
            Workspace = Assert.IsType<WorkspaceViewModel>(App.Shell.CurrentScreen);
            Sale = Assert.IsType<SalePageViewModel>(Workspace.CurrentPage);
            Window = App.ShowWindow();
            Window.VisibleTexts();
        }

        public TestApp App { get; } = new();
        public WorkspaceViewModel Workspace { get; }
        public SalePageViewModel Sale { get; }
        public Window Window { get; }

        public void Type(string text, bool enter = true)
        {
            Window.KeyTextInput(text);
            if (enter)
                Press(Key.Enter, PhysicalKey.Enter);
        }

        public void Press(Key key, PhysicalKey physical) => Window.KeyPress(key, RawInputModifiers.None, physical, null);

        public void Dispose() => App.Dispose();
    }

    [AvaloniaFact]
    public void Enter_WithEmptySearch_Pays_AndEnterAgainConfirms()
    {
        using var shop = new Shop();
        shop.Type("8410000000016");                    // el lector: código + Enter → añade, no cobra
        Assert.Single(shop.Sale.TicketLines);
        Assert.False(shop.Sale.IsDialogOpen);

        shop.Press(Key.Enter, PhysicalKey.Enter);       // buscador vacío → cobrar
        var payment = Assert.IsType<PaymentViewModel>(shop.Sale.Dialog);
        Assert.True(payment.IsCash);
        shop.Window.VisibleTexts();
        shop.Press(Key.Enter, PhysicalKey.Enter);       // importe justo

        Assert.False(shop.Sale.IsDialogOpen);
        Assert.Empty(shop.Sale.TicketLines);
        Assert.StartsWith("Ticket T2026-000001 cobrado", shop.Sale.Message);
    }

    [AvaloniaFact]
    public void Plus_PaysByCard()
    {
        using var shop = new Shop();
        shop.Type("8410000000016");

        shop.Type("+", enter: false);

        var payment = Assert.IsType<PaymentViewModel>(shop.Sale.Dialog);
        Assert.True(payment.IsCard);
        Assert.Equal("", shop.Sale.SearchText);         // el "+" no se queda escrito
        shop.Window.VisibleTexts();
        shop.Press(Key.Enter, PhysicalKey.Enter);
        Assert.False(shop.Sale.IsDialogOpen);
        Assert.Empty(shop.Sale.TicketLines);
    }

    [AvaloniaFact]
    public void QuantitySyntax_AddsOrRemovesUnitsOfTheLastLine()
    {
        using var shop = new Shop();
        shop.Type("8410000000016");

        shop.Type("4*");
        Assert.Equal(5, shop.Sale.TicketLines[^1].Quantity);
        shop.Type("-2*");
        Assert.Equal(3, shop.Sale.TicketLines[^1].Quantity);
        shop.Type("-9*");                                // quitar todas quita la línea
        Assert.Empty(shop.Sale.TicketLines);

        shop.Type("2*");
        Assert.Equal("No hay ninguna línea en el ticket.", shop.Sale.Message);
        shop.Type("3*8410000000016");                    // la sintaxis de siempre sigue funcionando
        Assert.Equal(3, shop.Sale.TicketLines[^1].Quantity);
    }

    [AvaloniaFact]
    public void PriceSyntax_AddsAnArticleOfThatPrice()
    {
        using var shop = new Shop();

        shop.Type("2,50");
        shop.Type("1.99");
        shop.Type("2*0,75");

        Assert.Equal([("Artículo", 1, "2,50 €"), ("Artículo", 1, "1,99 €"), ("Artículo", 2, "1,50 €")],
            shop.Sale.TicketLines.Select(l => (l.Description, l.Quantity, l.Total.Replace(' ', ' '))));
        Assert.Equal("5,99 €", shop.Sale.TotalText.Replace(' ', ' '));
    }

    [AvaloniaFact]
    public void Insert_OpensTheTill_ThenTheDrawer()
    {
        using var shop = new Shop(openCash: false, asAdmin: true);
        Assert.True(shop.Sale.IsCashClosed);

        shop.Press(Key.Insert, PhysicalKey.Insert);       // caja cerrada → abrirla (fondo vacío = 0 €)
        Assert.True(shop.Sale.IsCashOpen);

        shop.Window.VisibleTexts();
        shop.Press(Key.Insert, PhysicalKey.Insert);       // caja abierta → abrir el cajón (admin: sin PIN)
        Assert.False(shop.Sale.IsDialogOpen);
        Assert.Equal(1, shop.App.PrintedTickets);         // el pulso del cajón va por la "impresora"
    }

    [AvaloniaFact]
    public void Delete_RemovesTheLastLine_OnlyWithEmptySearch()
    {
        using var shop = new Shop();
        shop.Type("2,00");
        shop.Type("3,00");

        shop.Type("ab", enter: false);
        shop.Press(Key.Delete, PhysicalKey.Delete);       // con texto, Supr borra texto: no toca el ticket
        Assert.Equal(2, shop.Sale.TicketLines.Count);

        shop.Sale.SearchText = "";
        shop.Press(Key.Delete, PhysicalKey.Delete);
        Assert.Equal("2,00 €", Assert.Single(shop.Sale.TicketLines).Total.Replace(' ', ' '));
    }

    [AvaloniaFact]
    public void F1_ShowsTheHelp_AndF10SwitchesToKeyboardMode()
    {
        using var shop = new Shop();

        shop.Press(Key.F1, PhysicalKey.F1);
        var help = Assert.IsType<ShortcutHelpViewModel>(shop.Sale.Dialog);
        Assert.Contains(help.Rows, r => r.Keys == "+" && r.Description == "Cobrar con tarjeta");
        Assert.Contains(help.Rows, r => r.Keys == "-n*");
        help.CloseCommand.Execute(null);

        Assert.True(shop.Sale.IsTouchMode);
        shop.Window.VisibleTexts();
        shop.Press(Key.F10, PhysicalKey.F10);
        Assert.True(shop.Sale.IsKeyboardMode);
        Assert.False(shop.Sale.ShowProductTiles);
        Assert.False(shop.Sale.ShowCategories);
        Assert.Equal("Keyboard", shop.App.Get<SettingsStore>().Get(SettingKeys.SaleMode)); // se recuerda

        shop.Sale.SearchText = "Taz";                     // buscando por nombre sí se ven los productos
        Assert.True(shop.Sale.ShowProductTiles);
    }

    [AvaloniaFact]
    public void Settings_ChangeShortcuts_AndMode()
    {
        using var shop = new Shop(asAdmin: true);
        shop.Workspace.NavigateCommand.Execute(shop.Workspace.NavItems.Single(n => n.PageType == typeof(SettingsPageViewModel)));
        var settings = Assert.IsType<SettingsPageViewModel>(shop.Workspace.CurrentPage);
        var payCard = settings.ShortcutRows.Single(r => r.Action == ShortcutAction.PayCard);
        Assert.Equal("+", payCard.Keys);

        payCard.Keys = "F12";                             // ya es de "Cobrar"
        settings.SaveCommand.Execute(null);
        Assert.Equal("Hay una tecla repetida en dos atajos.", settings.Message);

        payCard.Keys = "x";                               // las letras se escriben en el buscador
        settings.SaveCommand.Execute(null);
        Assert.Equal("Hay un atajo que no se entiende o usa una tecla no permitida.", settings.Message);

        payCard.Keys = "/, Ctrl+T";
        settings.ShortcutRows.Single(r => r.Action == ShortcutAction.Help).Keys = "";
        settings.SaleMode = settings.SaleModes.Single(m => m.Value == "Keyboard");
        settings.SaveCommand.Execute(null);
        Assert.False(settings.MessageIsError, settings.Message);

        var store = shop.App.Get<ShortcutSettings>();
        Assert.Equal("/, Ctrl+T", store.GetText(ShortcutAction.PayCard));
        Assert.Empty(store.Get(ShortcutAction.Help));
        shop.Workspace.NavigateCommand.Execute(shop.Workspace.NavItems.Single(n => n.PageType == typeof(SalePageViewModel)));
        var sale = Assert.IsType<SalePageViewModel>(shop.Workspace.CurrentPage);
        Assert.True(sale.IsKeyboardMode);

        sale.SearchText = "2,00";
        sale.SubmitSearchCommand.Execute(null);
        shop.Window.VisibleTexts();
        shop.Window.KeyPress(Key.T, RawInputModifiers.Control, PhysicalKey.T, "t");
        Assert.True(Assert.IsType<PaymentViewModel>(sale.Dialog).IsCard);

        settings = shop.App.Get<SettingsPageViewModel>();
        settings.Load();
        settings.ResetShortcutsCommand.Execute(null);
        Assert.Equal("+", store.GetText(ShortcutAction.PayCard));
    }
}
