using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pos.Core.Domain;
using Pos.Core.Verifactu;
using Pos.Data;
using Pos.Modules.CashRegister;
using Pos.Modules.Invoicing;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;
using Pos.Modules.Verifactu;

namespace Pos.Modules.Tests;

/// <summary>Sección 5: registros Verifactu (FAC-04), certificado (VFA-01), envío (VFA-02) y cola (VFA-03).</summary>
public sealed class VerifactuModuleTests : IDisposable
{
    private static readonly ProducerInfo Producer = new("Productor de Prueba S.L.", "B12345674", "Calle 1, Madrid", "StarSeaPOS", "SS", "1.0.0", "01-10-2026", "Madrid");

    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly SettingsStore _settings;
    private readonly SalesService _sales;
    private readonly InvoiceService _invoices;
    private readonly CertificateStore _certificates;
    private readonly FakeTransport _transport = new();
    private readonly VerifactuSender _sender;
    private readonly int _userId;
    private readonly Product _taza;

    public VerifactuModuleTests()
    {
        _settings = new SettingsStore(_db.Factory);
        var profiles = new ProfileStore(_settings);
        profiles.SaveBusiness(InvoicingTests.Business);
        IInvoiceHook[] hooks = [new VerifactuRecorder(_clock)];
        _sales = new SalesService(_db.Factory, _clock, [new InvoiceSaleHook(profiles, hooks)]);
        _invoices = new InvoiceService(_db.Factory, profiles, _clock, hooks, _settings);
        _certificates = new CertificateStore(_settings, _clock);
        _sender = new VerifactuSender(_db.Factory, _settings, _certificates, _transport, Producer, _clock);
        _userId = new UserService(_db.Factory, _clock).CreateUser("Luis", "2222", Role.Cashier).Value!.Id;
        _taza = new CatalogService(_db.Factory).SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "111", null, null)).Value!;
        new CashRegisterService(_db.Factory, _clock).Open(_userId, 100m);
    }

    public void Dispose() => _db.Dispose();

    private Sale Sell(Pos.Core.Invoicing.InvoiceCustomer? customer = null)
    {
        var ticket = new Ticket();
        ticket.Add(TicketItem.FromProduct(_taza), 2);
        var result = _sales.Checkout(ticket, PaymentRequest.Cash(10m), _userId, customer);
        Assert.True(result.Success, result.ErrorKey);
        return result.Value!.Sale;
    }

    private List<VerifactuRecord> Records()
    {
        using var db = _db.Factory.CreateDbContext();
        return db.VerifactuRecords.AsNoTracking().OrderBy(r => r.Id).ToList();
    }

    private void Enable()
    {
        _settings.Set(VerifactuSettingKeys.Enabled, "true");
        Assert.True(_certificates.Save(TestCertificate.Pfx(), TestCertificate.Password).Success);
    }

    // --- FAC-04 ---

    [Fact]
    public void EverySale_GetsAChainedRecord()
    {
        Sell();
        Sell();

        var records = Records();
        Assert.Equal(2, records.Count);
        var (first, second) = (records[0], records[1]);
        Assert.Equal(("T2026-000001", "F2", "01-10-2026", 1.21m, 7.00m), (first.InvoiceNumber, first.InvoiceType, first.IssueDate, first.TotalVat, first.Total));
        Assert.Null(first.PreviousHash);
        Assert.Equal(first.Hash, second.PreviousHash);   // encadenamiento
        Assert.Equal(first.Id, second.PreviousRecordId);
        Assert.Equal(VerifactuHash.ForAlta(second.IssuerNif, second.InvoiceNumber, second.IssueDate, second.InvoiceType,
            second.TotalVat, second.Total, second.PreviousHash, second.GeneratedAt), second.Hash);
        Assert.All(records, r => Assert.Equal(VerifactuStatus.Pending, r.Status));
    }

    [Fact]
    public void CompleteInvoices_AreF1_AndInvoicedTicketsAreF3()
    {
        Sell(InvoicingTests.Customer);                    // F1
        var ticket = _invoices.GetCurrentForSale(Sell().Id)!; // F2
        _invoices.IssueFromTicket(ticket.InvoiceId, InvoicingTests.Customer); // F3 (FAC-06)

        Assert.Equal(["F1", "F2", "F3"], Records().Select(r => r.InvoiceType));
        var chain = Records();
        Assert.Equal(chain[1].Hash, chain[2].PreviousHash);
    }

    [Fact]
    public void Records_CannotBeAlteredOrDeleted_ButStatusCanChange()
    {
        Sell();
        using var db = _db.Factory.CreateDbContext();

        Assert.Throws<SqliteException>(() => db.VerifactuRecords.ExecuteDelete());
        Assert.Throws<SqliteException>(() => db.VerifactuRecords.ExecuteUpdate(s => s.SetProperty(r => r.Total, 0m)));
        Assert.Throws<SqliteException>(() => db.VerifactuRecords.ExecuteUpdate(s => s.SetProperty(r => r.Hash, "X")));
        db.VerifactuRecords.ExecuteUpdate(s => s.SetProperty(r => r.Status, VerifactuStatus.Accepted));
        Assert.Equal(VerifactuStatus.Accepted, Records()[0].Status);
    }

    [Fact]
    public void InvoiceDocument_CarriesTheAeatQr()
    {
        var doc = _invoices.GetCurrentForSale(Sell().Id)!;

        Assert.Equal("https://prewww2.aeat.es/wlpl/TIKE-CONT/ValidarQR?nif=B12345674&numserie=T2026-000001&fecha=01-10-2026&importe=7.00", doc.QrUrl);
        Assert.False(doc.VerifactuMode);
        Assert.Equal("T2026-000001", Assert.Single(_invoices.Search(doc.QrUrl!)).Code); // FAC-06: escanear el QR
    }

    // --- VFA-02: XML ---

    [Fact]
    public async Task Xml_HasTheRegistroAltaInSchemaOrder()
    {
        Sell();
        Sell(InvoicingTests.Customer);
        Enable();

        await _sender.SendPendingAsync();

        var xml = XDocument.Parse(_transport.LastBody!);
        var altas = xml.Descendants(VerifactuXml.Sum1 + "RegistroAlta").ToList();
        Assert.Equal(2, altas.Count);
        Assert.Equal(
            ["IDVersion", "IDFactura", "NombreRazonEmisor", "TipoFactura", "DescripcionOperacion", "Desglose", "CuotaTotal",
             "ImporteTotal", "Encadenamiento", "SistemaInformatico", "FechaHoraHusoGenRegistro", "TipoHuella", "Huella"],
            altas[0].Elements().Select(e => e.Name.LocalName));
        Assert.Equal("S", altas[0].Descendants(VerifactuXml.Sum1 + "PrimerRegistro").Single().Value);
        Assert.Equal(Records()[0].Hash, altas[1].Descendants(VerifactuXml.Sum1 + "RegistroAnterior").Single()
            .Element(VerifactuXml.Sum1 + "Huella")!.Value);
        Assert.Equal("Papelería Pérez S.L.", altas[1].Descendants(VerifactuXml.Sum1 + "Destinatarios").Single()
            .Descendants(VerifactuXml.Sum1 + "NombreRazon").Single().Value);
        Assert.Equal("21.00", altas[0].Descendants(VerifactuXml.Sum1 + "TipoImpositivo").Single().Value);
        Assert.Equal("B12345674", xml.Descendants(VerifactuXml.Sum1 + "ObligadoEmision").Single().Element(VerifactuXml.Sum1 + "NIF")!.Value);
    }

    [Fact]
    public async Task Xml_F3_ListsTheReplacedTicket()
    {
        var ticket = _invoices.GetCurrentForSale(Sell().Id)!;
        _invoices.IssueFromTicket(ticket.InvoiceId, InvoicingTests.Customer);
        Enable();

        await _sender.SendPendingAsync();

        var f3 = XDocument.Parse(_transport.LastBody!).Descendants(VerifactuXml.Sum1 + "RegistroAlta").Last();
        Assert.Equal("T2026-000001", f3.Descendants(VerifactuXml.Sum1 + "FacturasSustituidas").Single()
            .Descendants(VerifactuXml.Sum1 + "NumSerieFactura").Single().Value);
    }

    // --- VFA-02 / VFA-03: envío y cola ---

    [Fact]
    public async Task Disabled_DoesNotSend()
    {
        Sell();

        Assert.Equal(VerifactuSendResult.Nothing, await _sender.SendPendingAsync());
        Assert.Null(_transport.LastBody);
    }

    [Fact]
    public async Task WithoutCertificate_ReportsIt()
    {
        Sell();
        _settings.Set(VerifactuSettingKeys.Enabled, "true");

        Assert.Equal("ErrorVerifactuNoCertificate", (await _sender.SendPendingAsync()).ErrorKey);
    }

    [Fact]
    public async Task Response_UpdatesEachRecordStatus()
    {
        // VFA-02: se guarda la respuesta (aceptado, aceptado con errores, rechazado).
        Sell();
        Sell();
        Sell();
        Enable();
        _transport.Respond = numbers => FakeTransport.Response(
            ("T2026-000001", "Correcto", null),
            ("T2026-000002", "AceptadoConErrores", "2000"),
            ("T2026-000003", "Incorrecto", "1100"));

        var result = await _sender.SendPendingAsync();

        Assert.Equal((3, 2, 1), (result.Sent, result.Accepted, result.Rejected));
        Assert.Equal([VerifactuStatus.Accepted, VerifactuStatus.AcceptedWithErrors, VerifactuStatus.Rejected], Records().Select(r => r.Status));
        Assert.Equal("1100", Records()[2].ErrorCode);
        var status = _sender.GetStatus();
        Assert.Equal((0, 1, 1, 1), (status.Pending, status.Accepted, status.AcceptedWithErrors, status.Rejected));
    }

    [Fact]
    public async Task WaitTimeFromAeat_IsRespected()
    {
        // VFA-03: reintento respetando el tiempo de espera que indica la AEAT.
        Sell();
        Enable();
        _transport.WaitSeconds = 120;
        await _sender.SendPendingAsync();

        Sell();
        Assert.Equal(VerifactuSendResult.Nothing, await _sender.SendPendingAsync()); // aún no
        _clock.Advance(TimeSpan.FromSeconds(121));
        Assert.Equal(1, (await _sender.SendPendingAsync()).Sent);
    }

    [Fact]
    public async Task NoConnection_KeepsRecordsQueuedAndRetriesLater()
    {
        // VFA-03: sin internet se encola; la venta no se para.
        Enable();
        _transport.Fail = true;
        Sell();

        var result = await _sender.SendPendingAsync();

        Assert.Equal("ErrorVerifactuSend", result.ErrorKey);
        var record = Assert.Single(Records());
        Assert.Equal((VerifactuStatus.Pending, 1), (record.Status, record.Attempts));
        Assert.Equal(VerifactuSendResult.Nothing, await _sender.SendPendingAsync()); // espera antes de reintentar

        _transport.Fail = false;
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Equal(1, (await _sender.SendPendingAsync()).Accepted);
        Assert.Equal(VerifactuStatus.Accepted, Records()[0].Status);
    }

    [Fact]
    public async Task Duplicate_IsTreatedAsAccepted()
    {
        Sell();
        Enable();
        _transport.Respond = _ => FakeTransport.Response(("T2026-000001", "Incorrecto", VerifactuSender.DuplicateErrorCode));

        await _sender.SendPendingAsync();

        Assert.Equal(VerifactuStatus.Accepted, Records()[0].Status);
    }

    [Fact]
    public void SoapFault_IsAnError()
    {
        const string fault = """<env:Envelope xmlns:env="http://schemas.xmlsoap.org/soap/envelope/"><env:Body><env:Fault><faultcode>env:Client</faultcode><faultstring>Certificado no válido</faultstring></env:Fault></env:Body></env:Envelope>""";

        Assert.Equal("Certificado no válido", Assert.Throws<VerifactuServiceException>(() => VerifactuResponseParser.Parse(fault)).Message);
    }

    // --- VFA-01: certificado ---

    [Fact]
    public void Certificate_IsStoredEncryptedAndReadBack()
    {
        var result = _certificates.Save(TestCertificate.Pfx(), TestCertificate.Password);

        Assert.True(result.Success, result.ErrorKey);
        Assert.Equal(("12345678Z", "PRUEBA FICTICIO"), (result.Value!.Nif, result.Value.Subject));
        Assert.DoesNotContain(TestCertificate.Password, _settings.Get(VerifactuSettingKeys.CertificatePassword)!);
        using var loaded = _certificates.Load()!;
        Assert.True(loaded.HasPrivateKey);
    }

    [Fact]
    public void Certificate_WrongPasswordOrExpired()
    {
        Assert.Equal("ErrorCertificatePassword", _certificates.Save(TestCertificate.Pfx(), "otra").ErrorKey);
        Assert.Equal("ErrorCertificateExpired", _certificates.Save(TestCertificate.Pfx(expired: true), TestCertificate.Password).ErrorKey);
    }

    // --- Ayudantes ---

    private sealed class FakeTransport : IVerifactuTransport
    {
        public string? LastBody { get; private set; }

        public bool Fail { get; set; }

        public int WaitSeconds { get; set; } = 60;

        /// <summary>Por defecto, todo "Correcto".</summary>
        public Func<IReadOnlyList<string>, string>? Respond { get; set; }

        public Task<string> PostAsync(Uri endpoint, string soapXml, X509Certificate2 certificate, CancellationToken cancellationToken)
        {
            if (Fail)
                throw new HttpRequestException("No se puede conectar con el servidor remoto");
            LastBody = soapXml;
            var numbers = XDocument.Parse(soapXml).Descendants(VerifactuXml.Sum1 + "IDFactura")
                .Select(e => e.Element(VerifactuXml.Sum1 + "NumSerieFactura")!.Value).ToList();
            var response = Respond?.Invoke(numbers) ?? Response(numbers.Select(n => (n, "Correcto", (string?)null)).ToArray());
            return Task.FromResult(response.Replace("<TiempoEsperaEnvio>60<", $"<TiempoEsperaEnvio>{WaitSeconds}<"));
        }

        public Task TestAsync(Uri endpoint, X509Certificate2 certificate, CancellationToken cancellationToken) => Task.CompletedTask;

        public static string Response(params (string Number, string Status, string? Code)[] lines) =>
            $"""
            <env:Envelope xmlns:env="http://schemas.xmlsoap.org/soap/envelope/"><env:Body>
            <RespuestaRegFactuSistemaFacturacion><CSV>A-TEST</CSV><TiempoEsperaEnvio>60</TiempoEsperaEnvio><EstadoEnvio>Correcto</EstadoEnvio>
            {string.Concat(lines.Select(l => $"<RespuestaLinea><IDFactura><IDEmisorFactura>B12345674</IDEmisorFactura><NumSerieFactura>{l.Number}</NumSerieFactura></IDFactura><EstadoRegistro>{l.Status}</EstadoRegistro>{(l.Code is null ? "" : $"<CodigoErrorRegistro>{l.Code}</CodigoErrorRegistro><DescripcionErrorRegistro>Error {l.Code}</DescripcionErrorRegistro>")}</RespuestaLinea>"))}
            </RespuestaRegFactuSistemaFacturacion></env:Body></env:Envelope>
            """;
    }
}

/// <summary>Certificado autofirmado con clave privada, con un sujeto como el de la FNMT.</summary>
internal static class TestCertificate
{
    public const string Password = "prueba-1234";

    public static byte[] Pfx(bool expired = false)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=PRUEBA FICTICIO, SERIALNUMBER=IDCES-12345678Z, C=ES", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        using var certificate = expired
            ? request.CreateSelfSigned(from.AddYears(-3), from.AddYears(-1))
            : request.CreateSelfSigned(from, from.AddYears(4));
        return certificate.Export(X509ContentType.Pfx, Password);
    }
}
