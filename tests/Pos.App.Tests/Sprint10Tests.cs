using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Pos.App.Updates;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Data;
using Pos.Modules.CashRegister;
using Pos.Modules.Products;
using Pos.Modules.Users;

namespace Pos.App.Tests;

/// <summary>
/// Recorridos de la sección 10: idioma por usuario (CFG-02), ticket en el idioma del cliente (CFG-03),
/// modalidad Verifactu (VFA-04) y actualizaciones (CFG-06).
/// </summary>
public class Sprint10Tests
{
    private sealed class Shop : IDisposable
    {
        public Shop(Action<IServiceCollection>? overrides = null)
        {
            App = new TestApp(overrides);
            var users = App.Get<UserService>();
            Admin = users.CreateUser("Ana", "1111", Role.Admin).Value!;
            users.CreateUser("Luis", "2222", Role.Cashier);
            App.Get<CatalogService>().SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "8410000000016", null, null));
            App.Get<CashRegisterService>().Open(Admin.Id, 100m);
        }

        public TestApp App { get; }
        public User Admin { get; }
        public WorkspaceViewModel Workspace => Assert.IsType<WorkspaceViewModel>(App.Shell.CurrentScreen);

        public void SignIn(string name, string pin)
        {
            var login = Assert.IsType<LoginViewModel>(App.Shell.CurrentScreen);
            login.SelectUserCommand.Execute(login.Users.Single(u => u.Name == name));
            foreach (var d in pin)
                login.PinEntry.Digit(d.ToString());
        }

        public T Open<T>() where T : PageViewModel
        {
            Workspace.NavigateCommand.Execute(Workspace.NavItems.Single(n => n.PageType == typeof(T)));
            return Assert.IsType<T>(Workspace.CurrentPage);
        }

        public void Dispose() => App.Dispose();
    }

    // --- CFG-02 ---

    [AvaloniaFact]
    public void EachUserKeepsTheirLanguage()
    {
        using var shop = new Shop();
        var L = shop.App.Get<ILocalizer>();
        shop.SignIn("Luis", "2222");
        Assert.Equal("es", L.CurrentLanguage);

        shop.Workspace.MyLanguage = shop.Workspace.MyLanguages.Single(l => l.Code == "zh");
        Assert.Equal("zh", L.CurrentLanguage);

        shop.Workspace.LogoutCommand.Execute(null);
        Assert.Equal("es", L.CurrentLanguage);       // la pantalla de login, en el idioma de la tienda
        shop.SignIn("Ana", "1111");
        Assert.Equal("es", L.CurrentLanguage);       // el cambio de Luis no afecta a Ana
        shop.Workspace.LogoutCommand.Execute(null);

        shop.SignIn("Luis", "2222");
        Assert.Equal("zh", L.CurrentLanguage);       // se recuerda al volver a entrar
        Assert.Equal("zh", shop.Workspace.MyLanguage!.Code);

        shop.Workspace.MyLanguage = shop.Workspace.MyLanguages[0]; // vuelve al de la tienda
        Assert.Equal("es", L.CurrentLanguage);
    }

    // --- CFG-03 ---

    [AvaloniaFact]
    public async Task Payment_TicketInTheCustomersLanguage()
    {
        using var shop = new Shop();
        var profiles = shop.App.Get<ProfileStore>();
        profiles.SavePrinter(profiles.GetPrinter() with { AutoPrint = AutoPrintMode.Yes });
        shop.SignIn("Luis", "2222");
        var sale = shop.Open<SalePageViewModel>();
        sale.SearchText = "8410000000016";
        sale.SubmitSearchCommand.Execute(null);

        sale.ChargeCommand.Execute(null);
        var payment = Assert.IsType<PaymentViewModel>(sale.Dialog);
        Assert.Equal("es", payment.TicketLanguage!.Code); // por defecto, el idioma de impresión
        payment.TicketLanguage = payment.TicketLanguages.Single(l => l.Code == "en");
        payment.ConfirmCommand.Execute(null);
        await sale.LastPrint;

        var printed = Encoding.Latin1.GetString(File.ReadAllBytes(Directory.GetFiles(shop.App.PrintFolder, "*.bin").Single()));
        Assert.Contains("SIMPLIFIED INVOICE", printed);
        Assert.Contains("B12345674", printed);             // los datos fiscales no cambian
        Assert.Equal("es", shop.App.Get<ILocalizer>().CurrentLanguage);
    }

    // --- VFA-04 ---

    [AvaloniaFact]
    public void VerifactuPage_ModeChangeNeedsCertificate_AndShowsTheEventLog()
    {
        using var shop = new Shop();
        shop.SignIn("Ana", "1111");
        var page = shop.Open<VerifactuPageViewModel>();
        Assert.True(page.IsNoVeriFactu);

        page.Enabled = true;
        page.SaveCommand.Execute(null);

        Assert.Equal("Carga primero el certificado digital (Ajustes > Verifactu).", page.Message);
        Assert.True(page.IsNoVeriFactu); // no ha cambiado
        Assert.Equal(6, page.StatusFilters.Count);

        page.VerifyEventsCommand.Execute(null);
        Assert.Equal("El registro de eventos está intacto.", page.Message);
    }

    // --- CFG-06 ---

    private sealed class FakeLauncher : IInstallerLauncher
    {
        public string? Launched { get; private set; }

        public void Launch(string msiPath) => Launched = msiPath;
    }

    private sealed class FakeServer(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(files.TryGetValue(request.RequestUri!.ToString(), out var body)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private static (Shop Shop, FakeLauncher Launcher) ShopWithUpdate(string version, byte[] msi, string? sha = null)
    {
        var launcher = new FakeLauncher();
        var hash = sha ?? Convert.ToHexString(SHA256.HashData(msi));
        var files = new Dictionary<string, byte[]>
        {
            [UpdateService.DefaultFeedUrl] = Encoding.UTF8.GetBytes(
                $$"""{"version":"{{version}}","url":"https://example.org/StarSeaPOS-{{version}}.msi","sha256":"{{hash}}"}"""),
            [$"https://example.org/StarSeaPOS-{version}.msi"] = msi,
        };
        var shop = new Shop(services =>
        {
            services.AddSingleton<IInstallerLauncher>(launcher);
            services.AddSingleton<Func<HttpClient>>(_ => () => new HttpClient(new FakeServer(files)));
        });
        return (shop, launcher);
    }

    [AvaloniaFact]
    public async Task Update_IsFoundAndInstalledByAnAdmin()
    {
        var (shop, launcher) = ShopWithUpdate("99.0.0", [1, 2, 3, 4]);
        using var _ = shop;
        shop.SignIn("Ana", "1111");
        var about = shop.Open<AboutPageViewModel>();

        about.CheckUpdatesCommand.Execute(null);
        await about.LastOperation;
        Assert.Equal("Nueva versión disponible: 99.0.0", about.UpdateStatus);
        Assert.True(about.CanInstall);
        Assert.Equal("Nueva versión disponible: 99.0.0", shop.Workspace.UpdateNotice);

        about.InstallUpdateCommand.Execute(null);
        await about.LastOperation;

        Assert.NotNull(launcher.Launched);
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(launcher.Launched!));
        Assert.Equal("Instalando… la aplicación se cerrará y se abrirá la versión nueva.", about.UpdateStatus);
    }

    [AvaloniaFact]
    public async Task Update_WithWrongChecksum_IsNotInstalled()
    {
        var (shop, launcher) = ShopWithUpdate("99.0.0", [1, 2, 3, 4], sha: new string('0', 64));
        using var _ = shop;
        shop.SignIn("Ana", "1111");
        var about = shop.Open<AboutPageViewModel>();
        about.CheckUpdatesCommand.Execute(null);
        await about.LastOperation;

        about.InstallUpdateCommand.Execute(null);
        await about.LastOperation;

        Assert.Null(launcher.Launched);
        Assert.StartsWith("La descarga no coincide con la publicada", about.UpdateStatus);
    }

    [AvaloniaFact]
    public async Task Update_OlderVersionIsIgnored_AndCashiersCannotInstall()
    {
        var (shop, _) = ShopWithUpdate("0.0.1", [1]);
        using var __ = shop;
        shop.SignIn("Luis", "2222");
        var about = shop.Open<AboutPageViewModel>();

        about.CheckUpdatesCommand.Execute(null);
        await about.LastOperation;

        Assert.StartsWith("Tienes la última versión", about.UpdateStatus);
        Assert.False(about.CanInstall);
        Assert.Null(shop.Workspace.UpdateNotice);
    }
}
