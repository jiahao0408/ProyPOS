using Avalonia.Headless.XUnit;
using Microsoft.EntityFrameworkCore;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.CashRegister;
using Pos.Modules.Products;
using Pos.Modules.Users;

namespace Pos.App.Tests;

/// <summary>Recorridos de la sección 9: pantalla de cliente (HW-02) y cajón portamonedas (HW-03).</summary>
public class Sprint9Tests
{
    private static readonly byte[] DrawerKick = [0x1B, (byte)'p', 0, 25, 250];

    private sealed class Shop : IDisposable
    {
        public Shop(bool asAdmin = false, bool openDrawerOnCash = true)
        {
            var users = App.Get<UserService>();
            var admin = users.CreateUser("Ana", "1111", Role.Admin).Value!;
            users.CreateUser("Luis", "2222", Role.Cashier);
            App.Get<CatalogService>().SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "8410000000016", null, null));
            App.Get<CashRegisterService>().Open(admin.Id, 100m);
            var profiles = App.Get<ProfileStore>();
            profiles.SaveHardware(new HardwareProfile(openDrawerOnCash, CustomerDisplay: true, WelcomeMessage: ""));

            var login = Assert.IsType<LoginViewModel>(App.Shell.CurrentScreen);
            login.SelectUserCommand.Execute(login.Users.Single(u => u.Name == (asAdmin ? "Ana" : "Luis")));
            foreach (var d in asAdmin ? "1111" : "2222")
                login.PinEntry.Digit(d.ToString());
            Workspace = Assert.IsType<WorkspaceViewModel>(App.Shell.CurrentScreen);
            Workspace.NavigateCommand.Execute(Workspace.NavItems.Single(n => n.PageType == typeof(SalePageViewModel)));
            Sale = Assert.IsType<SalePageViewModel>(Workspace.CurrentPage);
        }

        public TestApp App { get; } = new();
        public WorkspaceViewModel Workspace { get; }
        public SalePageViewModel Sale { get; }
        public CustomerDisplayViewModel Display => App.Get<CustomerDisplayViewModel>();

        public void Scan(string code)
        {
            Sale.SearchText = code;
            Sale.SubmitSearchCommand.Execute(null);
        }

        public void Pay(bool card = false, string? cash = null)
        {
            Sale.ChargeCommand.Execute(null);
            var payment = (PaymentViewModel)Sale.Dialog!;
            if (card)
                payment.SetModeCommand.Execute(PaymentMode.Card);
            if (cash is not null)
                payment.CashTenderedText = cash;
            payment.ConfirmCommand.Execute(null);
        }

        /// <summary>Lo que ha recibido la "impresora" de fichero.</summary>
        public List<byte[]> Sent() => Directory.Exists(App.PrintFolder)
            ? Directory.GetFiles(App.PrintFolder, "*.bin").OrderBy(f => f).Select(File.ReadAllBytes).ToList()
            : [];

        public void Dispose() => App.Dispose();
    }

    private static bool Contains(byte[] bytes, byte[] sequence) =>
        Enumerable.Range(0, bytes.Length - sequence.Length + 1).Any(i => bytes.AsSpan(i, sequence.Length).SequenceEqual(sequence));

    // --- HW-03 ---

    [AvaloniaFact]
    public async Task Drawer_OpensOnCashPayment_NotOnCard()
    {
        using var shop = new Shop();
        shop.Scan("8410000000016");
        shop.Pay(card: true);
        await shop.Sale.LastDrawer;
        Assert.Empty(shop.Sent()); // con tarjeta no se abre

        shop.Scan("8410000000016");
        shop.Pay(cash: "5");
        await shop.Sale.LastDrawer;

        Assert.True(Contains(Assert.Single(shop.Sent()), DrawerKick));
    }

    [AvaloniaFact]
    public async Task Drawer_StaysClosedWhenTheOptionIsOff()
    {
        using var shop = new Shop(openDrawerOnCash: false);
        shop.Scan("8410000000016");
        shop.Pay();
        await shop.Sale.LastDrawer;

        Assert.Empty(shop.Sent());
    }

    [AvaloniaFact]
    public async Task Drawer_ManualOpeningByCashier_NeedsAdminPin_AndIsAudited()
    {
        using var shop = new Shop();

        shop.Sale.OpenDrawerCommand.Execute(null);
        var prompt = Assert.IsType<AdminPinPromptViewModel>(shop.Sale.Dialog);
        foreach (var d in "1111")
            prompt.PinEntry.Digit(d.ToString());
        await shop.Sale.LastDrawer;

        Assert.False(shop.Sale.IsDialogOpen);
        Assert.True(Contains(Assert.Single(shop.Sent()), DrawerKick));
        using var db = shop.App.Get<IDbContextFactory<PosDbContext>>().CreateDbContext();
        var audit = Assert.Single(db.AuditEntries);
        Assert.Equal((AuditActions.CashDrawer, "Luis", "Ana"), (audit.Action, audit.UserName, audit.AuthorizedBy));
    }

    [AvaloniaFact]
    public void Drawer_ManualOpeningCancelled_DoesNothing()
    {
        using var shop = new Shop();

        shop.Sale.OpenDrawerCommand.Execute(null);
        ((AdminPinPromptViewModel)shop.Sale.Dialog!).CancelCommand.Execute(null);

        Assert.Empty(shop.Sent());
    }

    // --- HW-02 ---

    [AvaloniaFact]
    public void CustomerDisplay_FollowsTheSale()
    {
        using var shop = new Shop();
        var display = shop.Display;
        Assert.True(display.IsEnabled);
        Assert.True(display.IsWelcome);
        Assert.Equal(("Bazar Estrella del Mar", "¡Bienvenido!"), (display.BusinessName, display.WelcomeText));

        shop.Scan("8410000000016");
        shop.Scan("8410000000016");
        Assert.True(display.IsTicket);
        var line = Assert.Single(display.Lines);
        Assert.Equal(("Taza", "2 ×", "7,00 €"), (line.Description, line.Quantity, line.Total.Replace(' ', ' ')));
        Assert.Equal("7,00 €", display.TotalText.Replace(' ', ' '));

        shop.Pay(cash: "10");
        Assert.True(display.IsThanks);
        Assert.Equal(("¡Gracias por su compra!", "Cambio: 3,00 €"), (display.ThanksText, display.ChangeText.Replace(' ', ' ')));

        shop.Scan("8410000000016");                 // empieza la siguiente venta
        Assert.True(display.IsTicket);
        shop.Sale.ClearTicketCommand.Execute(null);
        Assert.True(display.IsWelcome);
    }

    [AvaloniaFact]
    public void CustomerDisplay_SpeaksThePrintLanguage()
    {
        using var shop = new Shop();
        shop.App.Get<PrintLocalization>().Language = "en"; // el cajero trabaja en español; el cliente lee en inglés
        shop.Scan("8410000000016");

        shop.Pay(cash: "5");

        Assert.Equal("Thank you for your purchase!", shop.Display.ThanksText);
    }

    [AvaloniaFact]
    public void PrinterPage_TurnsTheDisplayOnAndOff()
    {
        using var shop = new Shop(asAdmin: true);
        shop.Workspace.NavigateCommand.Execute(shop.Workspace.NavItems.Single(n => n.PageType == typeof(PrinterPageViewModel)));
        var page = Assert.IsType<PrinterPageViewModel>(shop.Workspace.CurrentPage);
        Assert.True(page.CustomerDisplayEnabled);

        page.CustomerDisplayEnabled = false;
        page.WelcomeMessage = "Bienvenidos al bazar";
        page.SaveCommand.Execute(null);

        Assert.False(shop.Display.IsEnabled);
        Assert.Equal(new HardwareProfile(true, false, "Bienvenidos al bazar"), shop.App.Get<ProfileStore>().GetHardware());
        Assert.Equal("Bienvenidos al bazar", shop.Display.WelcomeText);
    }
}
