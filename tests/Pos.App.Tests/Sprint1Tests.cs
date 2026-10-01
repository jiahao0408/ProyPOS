using Avalonia.Headless.XUnit;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Core.Security;
using Pos.Data;
using Pos.Modules.Products;
using Pos.Modules.Users;

namespace Pos.App.Tests;

/// <summary>Recorridos completos de la sección 1 con la interfaz real.</summary>
public class Sprint1Tests
{
    private static void CreateUsers(TestApp app)
    {
        var users = app.Get<UserService>();
        users.CreateUser("Ana", "1111", Role.Admin);
        users.CreateUser("Luis", "2222", Role.Cashier);
    }

    private static void TypePin(PinEntryViewModel pad, string pin)
    {
        foreach (var digit in pin)
            pad.Digit(digit.ToString());
    }

    private static WorkspaceViewModel SignInAs(TestApp app, string name, string pin)
    {
        var login = Assert.IsType<LoginViewModel>(app.Shell.CurrentScreen);
        login.SelectUserCommand.Execute(login.Users.Single(u => u.Name == name));
        TypePin(login.PinEntry, pin);
        return Assert.IsType<WorkspaceViewModel>(app.Shell.CurrentScreen);
    }

    [AvaloniaFact]
    public void FirstRun_CreatesAdminAndSignsIn()
    {
        using var app = new TestApp();
        var window = app.ShowWindow();

        var firstRun = Assert.IsType<FirstRunViewModel>(app.Shell.CurrentScreen);
        Assert.Contains("Primer arranque", window.VisibleTexts());

        firstRun.Name = "Ana";
        firstRun.Pin = "1234";
        firstRun.PinConfirm = "1234";
        firstRun.CreateAdminCommand.Execute(null);

        var workspace = Assert.IsType<WorkspaceViewModel>(app.Shell.CurrentScreen);
        Assert.Equal("Ana", workspace.UserName);
        Assert.Equal("Administrador", workspace.RoleTitle);
    }

    [AvaloniaFact]
    public void FirstRun_RejectsDifferentPins()
    {
        using var app = new TestApp();
        var firstRun = Assert.IsType<FirstRunViewModel>(app.Shell.CurrentScreen);

        firstRun.Name = "Ana";
        firstRun.Pin = "1234";
        firstRun.PinConfirm = "4321";
        firstRun.CreateAdminCommand.Execute(null);

        Assert.IsType<FirstRunViewModel>(app.Shell.CurrentScreen);
        Assert.Equal("Los dos PIN no coinciden.", firstRun.Message);
    }

    [AvaloniaFact]
    public void Login_WrongPinShowsRemainingAttempts()
    {
        using var app = new TestApp();
        CreateUsers(app);
        var window = app.ShowWindow();
        var login = Assert.IsType<LoginViewModel>(app.Shell.CurrentScreen);

        login.SelectUserCommand.Execute(login.Users.Single(u => u.Name == "Luis"));
        TypePin(login.PinEntry, "9999");

        Assert.IsType<LoginViewModel>(app.Shell.CurrentScreen);
        Assert.Contains("PIN incorrecto. Quedan 4 intentos.", window.VisibleTexts());
    }

    [AvaloniaFact]
    public void Login_AndLogout()
    {
        using var app = new TestApp();
        CreateUsers(app);

        var workspace = SignInAs(app, "Luis", "2222");
        Assert.Equal("Cajero", workspace.RoleTitle);

        workspace.LogoutCommand.Execute(null);
        Assert.IsType<LoginViewModel>(app.Shell.CurrentScreen);
    }

    [AvaloniaFact]
    public void Inactivity_SignsOut()
    {
        // USR-01: cierre de sesión por inactividad (5 minutos por defecto).
        using var app = new TestApp();
        CreateUsers(app);
        SignInAs(app, "Luis", "2222");

        app.Clock.Advance(TimeSpan.FromMinutes(4));
        app.Shell.CheckInactivity();
        Assert.IsType<WorkspaceViewModel>(app.Shell.CurrentScreen);

        app.Clock.Advance(TimeSpan.FromMinutes(1));
        app.Shell.CheckInactivity();
        Assert.IsType<LoginViewModel>(app.Shell.CurrentScreen);
    }

    [AvaloniaFact]
    public void Inactivity_ActivityKeepsSessionOpen()
    {
        using var app = new TestApp();
        CreateUsers(app);
        SignInAs(app, "Luis", "2222");

        app.Clock.Advance(TimeSpan.FromMinutes(4));
        app.Shell.RegisterActivity();
        app.Clock.Advance(TimeSpan.FromMinutes(4));
        app.Shell.CheckInactivity();

        Assert.IsType<WorkspaceViewModel>(app.Shell.CurrentScreen);
    }

    [AvaloniaFact]
    public void Cashier_NeedsAdminPinForAdminSections()
    {
        // USR-02: las acciones de admin piden PIN de admin al cajero.
        using var app = new TestApp();
        CreateUsers(app);
        var workspace = SignInAs(app, "Luis", "2222");
        var products = workspace.NavItems.Single(n => n.PageType == typeof(ProductsPageViewModel));

        workspace.NavigateCommand.Execute(products);
        Assert.True(workspace.IsAdminPromptOpen);
        Assert.IsType<SalePageViewModel>(workspace.CurrentPage);

        TypePin(workspace.AdminPrompt!.PinEntry, "2222"); // PIN del propio cajero: no vale
        Assert.True(workspace.IsAdminPromptOpen);
        Assert.Equal("PIN de administrador incorrecto.", workspace.AdminPrompt.Message);

        TypePin(workspace.AdminPrompt.PinEntry, "1111");
        Assert.False(workspace.IsAdminPromptOpen);
        Assert.IsType<ProductsPageViewModel>(workspace.CurrentPage);
    }

    [AvaloniaFact]
    public void Admin_EntersAdminSectionsWithoutPrompt()
    {
        using var app = new TestApp();
        CreateUsers(app);
        var workspace = SignInAs(app, "Ana", "1111");

        workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == typeof(UsersPageViewModel)));

        Assert.False(workspace.IsAdminPromptOpen);
        Assert.IsType<UsersPageViewModel>(workspace.CurrentPage);
    }

    [AvaloniaFact]
    public void Sale_RequiresOpenCashRegister()
    {
        // CAJ-01: no se puede vender con la caja cerrada.
        using var app = new TestApp();
        CreateUsers(app);
        app.Get<CatalogService>().SaveCategory(null, "Hogar", 0, "#FF8800", false);
        var window = app.ShowWindow();
        var workspace = SignInAs(app, "Luis", "2222");
        var sale = Assert.IsType<SalePageViewModel>(workspace.CurrentPage);

        Assert.True(sale.IsCashClosed);
        Assert.Contains("La caja está cerrada", window.VisibleTexts());

        sale.OpeningFloatText = "150,50";
        sale.OpenCashCommand.Execute(null);

        Assert.True(sale.IsCashOpen);
        // PRE-02: las categorías salen como botones en la venta.
        Assert.Equal(["Todas", "Hogar"], sale.Categories.Select(c => c.Name));
    }

    [AvaloniaFact]
    public void Products_CreateAndSearch()
    {
        using var app = new TestApp();
        CreateUsers(app);
        var workspace = SignInAs(app, "Ana", "1111");
        workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == typeof(ProductsPageViewModel)));
        var page = Assert.IsType<ProductsPageViewModel>(workspace.CurrentPage);

        page.Name = "Taza de café";
        page.PriceText = "3,50";
        page.SelectedVat = page.VatOptions.Single(v => v.Rate == 21m);
        page.Barcode = "8410000000001";
        page.SaveCommand.Execute(null);

        Assert.Equal("Guardado.", page.Message);
        page.SearchText = "café";
        var row = Assert.Single(page.Products);
        Assert.Equal("3,50 €", row.Price.Replace(' ', ' '));
    }

    [AvaloniaFact]
    public void Products_ShowsTranslatedValidationErrors()
    {
        using var app = new TestApp();
        CreateUsers(app);
        var workspace = SignInAs(app, "Ana", "1111");
        workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == typeof(ProductsPageViewModel)));
        var page = Assert.IsType<ProductsPageViewModel>(workspace.CurrentPage);

        page.PriceText = "1";
        page.SaveCommand.Execute(null);

        Assert.True(page.MessageIsError);
        Assert.Equal("El nombre es obligatorio.", page.Message);
    }

    [AvaloniaFact]
    public void Settings_ChangeLanguageUpdatesTextsWithoutRestart()
    {
        // CFG-01: cambia todos los textos sin reiniciar.
        using var app = new TestApp();
        CreateUsers(app);
        var window = app.ShowWindow();
        var workspace = SignInAs(app, "Ana", "1111");
        workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == typeof(SettingsPageViewModel)));
        var settings = Assert.IsType<SettingsPageViewModel>(workspace.CurrentPage);
        Assert.Contains("Cerrar sesión", window.VisibleTexts());

        settings.SelectedLanguage = settings.Languages.Single(l => l.Code == "en");
        settings.SaveCommand.Execute(null);
        window.UpdateLayout();

        var texts = window.VisibleTexts();
        Assert.Contains("Log out", texts);          // barra superior (binding a L[...])
        Assert.Contains("Products", texts);         // menú lateral
        Assert.Contains("Administrator", texts);    // rol del usuario
        Assert.DoesNotContain("Cerrar sesión", texts);
        Assert.Equal("en", app.Get<SettingsStore>().Get(SettingKeys.Language));
    }

    [AvaloniaFact]
    public void Settings_CurrencyIsEuroByDefaultAndConfigurable()
    {
        // CFG-04: euro por defecto.
        using var app = new TestApp();
        CreateUsers(app);
        var workspace = SignInAs(app, "Ana", "1111");
        workspace.NavigateCommand.Execute(workspace.NavItems.Single(n => n.PageType == typeof(SettingsPageViewModel)));
        var settings = Assert.IsType<SettingsPageViewModel>(workspace.CurrentPage);

        Assert.Equal("€", settings.CurrencySymbol);

        settings.CurrencySymbol = "£";
        settings.SaveCommand.Execute(null);

        Assert.StartsWith("1.234,50", settings.Preview);
        Assert.Contains("£", settings.Preview);
    }

    [AvaloniaFact]
    public void Language_IsRestoredFromSettingsOnStartup()
    {
        using var app = new TestApp();
        app.Get<SettingsStore>().Set(SettingKeys.Language, "zh");

        // ILocalizer se crea al pedirlo por primera vez, como al arrancar la app.
        Assert.Equal("zh", app.Get<ILocalizer>().CurrentLanguage);
    }

    [AvaloniaFact]
    public void Shell_ShowsLoginAfterAdminExists()
    {
        using var app = new TestApp();
        CreateUsers(app);

        Assert.IsType<LoginViewModel>(app.Shell.CurrentScreen);
        Assert.Null(app.Get<ISession>().CurrentUser);
    }
}
