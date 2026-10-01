using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Avalonia.Headless.XUnit;
using Microsoft.Extensions.DependencyInjection;
using Pos.App.ViewModels;
using Pos.Core.Domain;
using Pos.Core.Verifactu;
using Pos.Data;
using Pos.Modules.CashRegister;
using Pos.Modules.Products;
using Pos.Modules.Users;
using Pos.Modules.Verifactu;

namespace Pos.App.Tests;

/// <summary>Recorridos de la sección 5 (Verifactu) con la interfaz real y una AEAT simulada.</summary>
public class Sprint5Tests
{
    private sealed class FakeAeat : IVerifactuTransport
    {
        public bool Offline { get; set; }

        public int Received { get; private set; }

        public Task<string> PostAsync(Uri endpoint, string soapXml, X509Certificate2 certificate, CancellationToken cancellationToken)
        {
            if (Offline)
                throw new HttpRequestException("Sin conexión");
            var numbers = System.Xml.Linq.XDocument.Parse(soapXml).Descendants(VerifactuXml.Sum1 + "IDFactura")
                .Select(e => e.Element(VerifactuXml.Sum1 + "NumSerieFactura")!.Value).ToList();
            Received += numbers.Count;
            return Task.FromResult(
                "<Envelope><Body><R><TiempoEsperaEnvio>60</TiempoEsperaEnvio><EstadoEnvio>Correcto</EstadoEnvio>" +
                string.Concat(numbers.Select(n => $"<RespuestaLinea><IDFactura><NumSerieFactura>{n}</NumSerieFactura></IDFactura><EstadoRegistro>Correcto</EstadoRegistro></RespuestaLinea>")) +
                "</R></Body></Envelope>");
        }

        public Task TestAsync(Uri endpoint, X509Certificate2 certificate, CancellationToken cancellationToken) =>
            Offline ? throw new HttpRequestException("Sin conexión") : Task.CompletedTask;
    }

    private sealed class Shop : IDisposable
    {
        public Shop()
        {
            App = new TestApp(services => services.AddSingleton<IVerifactuTransport>(Aeat));
            var users = App.Get<UserService>();
            var admin = users.CreateUser("Ana", "1111", Role.Admin).Value!;
            App.Get<CatalogService>().SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "8410000000016", null, null));
            App.Get<CashRegisterService>().Open(admin.Id, 100m);
            var login = Assert.IsType<LoginViewModel>(App.Shell.CurrentScreen);
            login.SelectUserCommand.Execute(login.Users.Single());
            foreach (var d in "1111")
                login.PinEntry.Digit(d.ToString());
            Workspace = Assert.IsType<WorkspaceViewModel>(App.Shell.CurrentScreen);
        }

        public FakeAeat Aeat { get; } = new();
        public TestApp App { get; }
        public WorkspaceViewModel Workspace { get; }

        public T Open<T>() where T : PageViewModel
        {
            Workspace.NavigateCommand.Execute(Workspace.NavItems.Single(n => n.PageType == typeof(T)));
            return Assert.IsType<T>(Workspace.CurrentPage);
        }

        public void Sell()
        {
            var sale = Open<SalePageViewModel>();
            sale.SearchText = "8410000000016";
            sale.SubmitSearchCommand.Execute(null);
            sale.ChargeCommand.Execute(null);
            ((PaymentViewModel)sale.Dialog!).ConfirmCommand.Execute(null);
        }

        public VerifactuPageViewModel ConfigureVerifactu()
        {
            var page = Open<VerifactuPageViewModel>();
            page.CertificatePassword = "prueba-1234";
            page.LoadCertificate(Certificate());
            page.Enabled = true;
            page.SaveCommand.Execute(null);
            return page;
        }

        public void Dispose() => App.Dispose();
    }

    private static byte[] Certificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=BAZAR ESTRELLA, OID.2.5.4.97=VATES-B12345674, C=ES", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var cert = request.CreateSelfSigned(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero));
        return cert.Export(X509ContentType.Pfx, "prueba-1234");
    }

    [AvaloniaFact]
    public void Enabling_RequiresACertificate()
    {
        using var shop = new Shop();
        var page = shop.Open<VerifactuPageViewModel>();

        page.Enabled = true;
        page.SaveCommand.Execute(null);

        Assert.True(page.MessageIsError);
        Assert.StartsWith("Carga primero el certificado", page.Message);
    }

    [AvaloniaFact]
    public async Task Certificate_LoadAndTestConnection()
    {
        // VFA-01: certificado guardado cifrado; botón de prueba contra el entorno de pruebas.
        using var shop = new Shop();
        var page = shop.ConfigureVerifactu();

        Assert.Contains("NIF B12345674", page.CertificateInfo);
        Assert.Equal("Pruebas", page.Environment!.Title);

        page.TestConnectionCommand.Execute(null);
        await page.LastOperation;
        Assert.Equal("Conexión con la AEAT correcta: el certificado es válido.", page.Message);

        shop.Aeat.Offline = true;
        page.TestConnectionCommand.Execute(null);
        await page.LastOperation;
        Assert.Equal("No se pudo conectar con la AEAT: Sin conexión", page.Message);
    }

    [AvaloniaFact]
    public async Task Sales_AreQueuedOfflineAndSentLater()
    {
        // VFA-03: sin internet no se para de vender; los registros esperan en la cola.
        using var shop = new Shop();
        shop.ConfigureVerifactu();
        shop.Aeat.Offline = true;

        shop.Sell();
        shop.Sell();
        var page = shop.Open<VerifactuPageViewModel>();
        Assert.StartsWith("Pendientes: 2", page.QueueSummary);

        page.SendNowCommand.Execute(null);
        await page.LastOperation;
        Assert.True(page.MessageIsError);

        shop.Aeat.Offline = false;
        page.SendNowCommand.Execute(null);
        await page.LastOperation;

        Assert.Equal("Enviados 2: 2 aceptados, 0 rechazados.", page.Message);
        Assert.StartsWith("Pendientes: 0 · Aceptados: 2", page.QueueSummary);
        Assert.Equal(2, shop.Aeat.Received);
        Assert.All(page.Records, r => Assert.Equal("Aceptado", r.Status));
    }

    [AvaloniaFact]
    public async Task Ticket_PrintsAeatQrAndVerifactuLegend()
    {
        // FAC-04: cada factura lleva QR; en modalidad VERI*FACTU, con su leyenda.
        using var shop = new Shop();
        shop.ConfigureVerifactu();
        var profiles = shop.App.Get<ProfileStore>();
        profiles.SavePrinter(profiles.GetPrinter() with { AutoPrint = AutoPrintMode.Yes });

        shop.Sell();
        await shop.Open<SalePageViewModel>().LastPrint;
        var tickets = shop.Open<TicketsPageViewModel>();
        tickets.Selected = tickets.Invoices.Single();

        Assert.Contains("[QR: https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR?nif=B12345674&numserie=T2026-000001", tickets.Preview);
        Assert.Contains(VerifactuQr.VerifactuLegend, tickets.Preview);
    }

    [AvaloniaFact]
    public void About_ShowsResponsibleDeclaration()
    {
        // VFA-06: texto con versión, fabricante y fecha, accesible desde la app.
        using var shop = new Shop();

        var about = shop.Open<AboutPageViewModel>();

        Assert.Contains("DECLARACIÓN RESPONSABLE", about.Declaration);
        Assert.Contains("StarSeaPOS", about.Declaration);
        Assert.Contains($"Versión: {about.Producer.Version}", about.Declaration);
        Assert.Contains("Orden HAC/1177/2024", about.Declaration);
        Assert.Equal("1.0.0", about.Producer.Version); // la de StarSeaPOS, no la del proceso de tests
        Assert.True(about.IsIncomplete); // producer.json aún con [COMPLETAR]
    }
}
