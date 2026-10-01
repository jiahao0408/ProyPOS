using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Pos.Core.Domain;
using Pos.Core.Verifactu;
using Pos.Data;
using Pos.Modules.CashRegister;
using Pos.Modules.DataTransfer;
using Pos.Modules.Invoicing;
using Pos.Modules.Products;
using Pos.Modules.Sales;
using Pos.Modules.Users;
using Pos.Modules.Verifactu;

namespace Pos.Modules.Tests;

/// <summary>Sección 10: modalidad VERI*FACTU / No VERI*FACTU (VFA-04) y panel con subsanaciones (VFA-05).</summary>
public sealed class Section10Tests : IDisposable
{
    private static readonly ProducerInfo Producer = new("Productor de Prueba S.L.", "B12345674", "Calle 1, Madrid", "StarSeaPOS", "SS", "1.0.0", "01-10-2026", "Madrid");

    private readonly TestDatabase _db = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero));
    private readonly SettingsStore _settings;
    private readonly SalesService _sales;
    private readonly CertificateStore _certificates;
    private readonly FakeAeat _aeat = new();
    private readonly VerifactuSender _sender;
    private readonly VerifactuSigner _signer;
    private readonly VerifactuModeService _modes;
    private readonly User _ana;
    private readonly Product _taza;

    public Section10Tests()
    {
        _settings = new SettingsStore(_db.Factory);
        var profiles = new ProfileStore(_settings);
        profiles.SaveBusiness(InvoicingTests.Business);
        IInvoiceHook[] hooks = [new VerifactuRecorder(_clock)];
        _sales = new SalesService(_db.Factory, _clock, [new InvoiceSaleHook(profiles, hooks)]);
        _certificates = new CertificateStore(_settings, _clock);
        _sender = new VerifactuSender(_db.Factory, _settings, _certificates, _aeat, Producer, _clock);
        _signer = new VerifactuSigner(_db.Factory, _certificates, _sender, Producer, _clock);
        _modes = new VerifactuModeService(_db.Factory, _settings, _certificates, _clock);
        _ana = new UserService(_db.Factory, _clock).CreateUser("Ana", "1111", Role.Admin).Value!;
        _taza = new CatalogService(_db.Factory).SaveProduct(null, new ProductInput("Taza", 3.50m, 21m, "111", null, null)).Value!;
        new CashRegisterService(_db.Factory, _clock).Open(_ana.Id, 100m);
    }

    public void Dispose() => _db.Dispose();

    private void Sell()
    {
        var ticket = new Ticket();
        ticket.Add(TicketItem.FromProduct(_taza));
        Assert.True(_sales.Checkout(ticket, PaymentRequest.Cash(5m), _ana.Id).Success);
        _clock.Advance(TimeSpan.FromMinutes(1));
    }

    private void LoadCertificate() => Assert.True(_certificates.Save(TestCertificate.Pfx(), TestCertificate.Password).Success);

    private List<VerifactuRecord> Records()
    {
        using var db = _db.Factory.CreateDbContext();
        return db.VerifactuRecords.AsNoTracking().OrderBy(r => r.Id).ToList();
    }

    private List<VerifactuEvent> Events()
    {
        using var db = _db.Factory.CreateDbContext();
        return db.VerifactuEvents.AsNoTracking().OrderBy(e => e.Id).ToList();
    }

    // --- VFA-04: No VERI*FACTU ---

    [Fact]
    public async Task NoVeriFactu_RecordsAreNotSent_ButSigned()
    {
        Assert.Equal(VerifactuMode.NoVeriFactu, _modes.Mode); // sin elegir, no se envía nada
        Sell();
        Sell();

        Assert.All(Records(), r => Assert.Equal(VerifactuStatus.NotSent, r.Status));
        Assert.Equal(VerifactuSendResult.Nothing, await _sender.SendPendingAsync());

        // Sin certificado no se puede firmar: queda la anomalía en el registro de eventos (una vez).
        Assert.Equal(0, _signer.SignPending());
        Assert.Equal(0, _signer.SignPending());
        Assert.Equal(VerifactuEventTypes.SignatureMissing, Assert.Single(Events()).Type);

        LoadCertificate();
        Assert.Equal(2, _signer.SignPending());
        Assert.Equal(0, _signer.UnsignedCount());

        var signature = _signer.GetSignature(Records()[0].Id)!;
        Assert.True(XadesSigner.Verify(signature.SignedXml));
        var xml = XDocument.Parse(signature.SignedXml);
        Assert.Equal("RegistroAlta", xml.Root!.Name.LocalName);
        Assert.Equal(Records()[0].Hash, xml.Root.Element(VerifactuXml.Sum1 + "Huella")!.Value);
        Assert.NotNull(xml.Descendants().SingleOrDefault(e => e.Name.LocalName == "SigningTime"));
    }

    [Fact]
    public void Signature_DetectsAnyChange_AndCannotBeAltered()
    {
        LoadCertificate();
        Sell();
        _signer.SignPending();
        var signed = _signer.GetSignature(Records()[0].Id)!.SignedXml;

        var tampered = signed.Replace("ImporteTotal>3.50<", "ImporteTotal>1.50<");
        Assert.NotEqual(signed, tampered);
        Assert.False(XadesSigner.Verify(tampered));
        using var db = _db.Factory.CreateDbContext();
        Assert.Throws<SqliteException>(() => db.VerifactuSignatures.ExecuteUpdate(s => s.SetProperty(x => x.SignedXml, "x")));
        Assert.Throws<SqliteException>(() => db.VerifactuSignatures.ExecuteDelete());
    }

    [Fact]
    public void ModeChange_IsRecorded_AndNeedsACertificateForVeriFactu()
    {
        Assert.Equal("ErrorVerifactuNoCertificate", _modes.SetMode(VerifactuMode.VeriFactu, _ana).ErrorKey);
        LoadCertificate();

        Assert.True(_modes.SetMode(VerifactuMode.VeriFactu, _ana).Success);

        Assert.Equal(VerifactuMode.VeriFactu, _modes.Mode);
        var e = Assert.Single(Events());
        Assert.Equal((VerifactuEventTypes.ModeChanged, "Ana"), (e.Type, e.UserName));
        using var db = _db.Factory.CreateDbContext();
        Assert.Equal(AuditActions.VerifactuMode, Assert.Single(db.AuditEntries).Action);
        Sell();
        Assert.Equal(VerifactuStatus.Pending, Records()[0].Status); // ahora sí se envía
    }

    [Fact]
    public async Task AfterSendingThisYear_CannotGoBackToNoVeriFactu()
    {
        LoadCertificate();
        _modes.SetMode(VerifactuMode.VeriFactu, _ana);
        Sell();
        await _sender.SendPendingAsync();

        Assert.Equal("ErrorVerifactuModeLocked", _modes.SetMode(VerifactuMode.NoVeriFactu, _ana).ErrorKey);
        Assert.Equal(VerifactuMode.VeriFactu, _modes.Mode);
    }

    [Fact]
    public void EventLog_IsChained_AndAlterationsAreDetected()
    {
        _modes.RecordStartup("1.0.0");
        _modes.RecordStartup("1.0.0");
        Assert.Null(_modes.VerifyEvents());

        var events = Events();
        Assert.Equal(events[0].Hash, events[1].PreviousHash);
        events[0].Details = "otra cosa";
        Assert.Equal(events[0].Id, VerifactuEventLog.FirstBroken(events));
        using var db = _db.Factory.CreateDbContext();
        Assert.Throws<SqliteException>(() => db.VerifactuEvents.ExecuteUpdate(s => s.SetProperty(x => x.Details, "x")));
    }

    [Fact]
    public void Startup_IsOnlyLoggedInNoVeriFactu()
    {
        LoadCertificate();
        _modes.SetMode(VerifactuMode.VeriFactu, _ana);

        _modes.RecordStartup("1.0.0");

        Assert.DoesNotContain(Events(), e => e.Type == VerifactuEventTypes.Startup);
    }

    // --- VFA-05 ---

    [Fact]
    public async Task Correction_OfARejectedRecord_IsChainedAndFlagged()
    {
        LoadCertificate();
        _modes.SetMode(VerifactuMode.VeriFactu, _ana);
        Sell();
        Sell();
        _aeat.Statuses["T2026-000001"] = ("Incorrecto", "1100");
        _aeat.Statuses["T2026-000002"] = ("AceptadoConErrores", "2000");
        await _sender.SendPendingAsync();
        var (rejected, withErrors) = (Records()[0], Records()[1]);

        var correction = _sender.CreateCorrection(rejected.Id, _ana);

        Assert.True(correction.Success, correction.ErrorKey);
        var c = Records().Last();
        Assert.Equal((rejected.Id, true, VerifactuStatus.Pending, "T2026-000001"), (c.CorrectsRecordId!.Value, c.PreviouslyRejected, c.Status, c.InvoiceNumber));
        Assert.Equal(withErrors.Hash, c.PreviousHash); // encadenado al último
        Assert.Equal("ErrorVerifactuAlreadyCorrected", _sender.CreateCorrection(rejected.Id, _ana).ErrorKey);
        Assert.Equal([rejected.Id], _sender.CorrectedRecordIds());

        _aeat.Statuses.Clear();
        _clock.Advance(TimeSpan.FromMinutes(2));
        await _sender.SendPendingAsync();
        var sent = XDocument.Parse(_aeat.LastBody!).Descendants(VerifactuXml.Sum1 + "RegistroAlta").Single();
        Assert.Equal("S", sent.Element(VerifactuXml.Sum1 + "Subsanacion")!.Value);
        Assert.Equal("S", sent.Element(VerifactuXml.Sum1 + "RechazoPrevio")!.Value);
        Assert.Equal(VerifactuStatus.Accepted, Records().Last().Status);

        // Aceptado con errores: subsanación sin "rechazo previo".
        Assert.True(_sender.CreateCorrection(withErrors.Id, _ana).Success);
        Assert.False(Records().Last().PreviouslyRejected);
    }

    [Fact]
    public async Task Correction_OnlyForProblems()
    {
        LoadCertificate();
        _modes.SetMode(VerifactuMode.VeriFactu, _ana);
        Sell();
        await _sender.SendPendingAsync();

        Assert.Equal("ErrorVerifactuNothingToCorrect", _sender.CreateCorrection(Records()[0].Id, _ana).ErrorKey);
        Assert.Equal("ErrorVerifactuRecordNotFound", _sender.CreateCorrection(999, _ana).ErrorKey);
        Assert.Equal([VerifactuStatus.Accepted], _sender.GetRecent(status: VerifactuStatus.Accepted).Select(r => r.Status));
        Assert.Empty(_sender.GetRecent(status: VerifactuStatus.Rejected));
    }

    // --- DAT-04 con el registro de eventos ---

    [Fact]
    public void BillingExport_IncludesTheEventLog_AndAnomaliesAreLogged()
    {
        Sell();
        var folder = Directory.CreateTempSubdirectory("starseapos-s10-").FullName;
        try
        {
            var export = new BillingRecordExport(_db.Factory, _clock);
            var path = Path.Combine(folder, "registro.zip");

            export.Export(path);

            Assert.Equal(VerifactuEventTypes.Export, Events().Last().Type);
            Assert.True(export.Verify(path).IsValid);

            File.WriteAllText(path, "no es un zip");
            export.Verify(path);
            Assert.Equal(VerifactuEventTypes.Export, Events().Last().Type); // un fichero que no es una exportación no es una anomalía
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>AEAT simulada: por defecto todo "Correcto"; se puede indicar el estado de cada factura.</summary>
    private sealed class FakeAeat : IVerifactuTransport
    {
        public Dictionary<string, (string Status, string? Code)> Statuses { get; } = [];

        public string? LastBody { get; private set; }

        public Task<string> PostAsync(Uri endpoint, string soapXml, X509Certificate2 certificate, CancellationToken cancellationToken)
        {
            LastBody = soapXml;
            var lines = XDocument.Parse(soapXml).Descendants(VerifactuXml.Sum1 + "IDFactura")
                .Select(e => e.Element(VerifactuXml.Sum1 + "NumSerieFactura")!.Value)
                .Select(n => Statuses.TryGetValue(n, out var s) ? (n, s.Status, s.Code) : (n, "Correcto", (string?)null));
            return Task.FromResult($"""
                <env:Envelope xmlns:env="http://schemas.xmlsoap.org/soap/envelope/"><env:Body>
                <RespuestaRegFactuSistemaFacturacion><TiempoEsperaEnvio>60</TiempoEsperaEnvio><EstadoEnvio>Correcto</EstadoEnvio>
                {string.Concat(lines.Select(l => $"<RespuestaLinea><IDFactura><NumSerieFactura>{l.Item1}</NumSerieFactura></IDFactura><EstadoRegistro>{l.Item2}</EstadoRegistro>{(l.Item3 is null ? "" : $"<CodigoErrorRegistro>{l.Item3}</CodigoErrorRegistro>")}</RespuestaLinea>"))}
                </RespuestaRegFactuSistemaFacturacion></env:Body></env:Envelope>
                """);
        }

        public Task TestAsync(Uri endpoint, X509Certificate2 certificate, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
