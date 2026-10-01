using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Pos.Core;
using Pos.Core.Domain;
using Pos.Core.Verifactu;
using Pos.Data;

namespace Pos.Modules.Verifactu;

/// <summary>Conexión HTTPS con certificado de cliente; separada para poder probar sin la AEAT.</summary>
public interface IVerifactuTransport
{
    Task<string> PostAsync(Uri endpoint, string soapXml, X509Certificate2 certificate, CancellationToken cancellationToken);

    /// <summary>VFA-01: comprueba que la AEAT acepta el certificado (descarga la descripción del servicio).</summary>
    Task TestAsync(Uri endpoint, X509Certificate2 certificate, CancellationToken cancellationToken);
}

public sealed class HttpVerifactuTransport : IVerifactuTransport
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    public async Task<string> PostAsync(Uri endpoint, string soapXml, X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        using var client = CreateClient(certificate);
        using var content = new StringContent(soapXml, Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
        request.Headers.Add("SOAPAction", "\"\"");
        using var response = await client.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        // Un SOAP Fault llega con HTTP 500 pero con cuerpo XML: lo interpreta el analizador de la respuesta.
        if (!response.IsSuccessStatusCode && !body.Contains("Fault", StringComparison.Ordinal))
            throw new HttpRequestException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
        return body;
    }

    public async Task TestAsync(Uri endpoint, X509Certificate2 certificate, CancellationToken cancellationToken)
    {
        using var client = CreateClient(certificate);
        using var response = await client.GetAsync(new Uri(endpoint + "?wsdl"), cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private static HttpClient CreateClient(X509Certificate2 certificate)
    {
        var handler = new HttpClientHandler { ClientCertificateOptions = ClientCertificateOption.Manual };
        handler.ClientCertificates.Add(certificate);
        return new HttpClient(handler, disposeHandler: true) { Timeout = Timeout };
    }
}

public sealed record VerifactuSendResult(int Sent, int Accepted, int Rejected, string? ErrorKey, string? ErrorDetail)
{
    public static VerifactuSendResult Nothing { get; } = new(0, 0, 0, null, null);
}

public sealed record VerifactuQueueStatus(int Pending, int Accepted, int AcceptedWithErrors, int Rejected, DateTime? NextSendAfterUtc, string? LastError,
    int NotSent = 0);

/// <summary>
/// VFA-02 y VFA-03: envía a la AEAT los registros pendientes, por orden, hasta 1000 por mensaje.
/// Respeta el tiempo de espera entre envíos que devuelve la AEAT y, si no hay conexión, deja los
/// registros en la cola y reintenta más tarde (cada vez esperando más, hasta una hora). La venta
/// nunca espera a la AEAT: los registros ya están guardados.
/// </summary>
public sealed class VerifactuSender(
    IDbContextFactory<PosDbContext> dbFactory,
    SettingsStore settings,
    CertificateStore certificates,
    IVerifactuTransport transport,
    ProducerInfo producer,
    TimeProvider clock)
{
    public const string ProductionEndpoint = "https://www1.agenciatributaria.gob.es/wlpl/TIKE-CONT/ws/SistemaFacturacion/VerifactuSOAP";
    public const string TestEndpoint = "https://prewww1.aeat.es/wlpl/TIKE-CONT/ws/SistemaFacturacion/VerifactuSOAP";

    /// <summary>Código de error de la AEAT para un registro que ya tenía: se da por aceptado.</summary>
    public const string DuplicateErrorCode = "3000";

    private static readonly SemaphoreSlim OneAtATime = new(1, 1);

    public bool IsEnabled => settings.Get(VerifactuSettingKeys.Enabled) == "true";

    public bool IsProduction => settings.Get(VerifactuSettingKeys.Environment) == VerifactuSettingKeys.Production;

    public Uri Endpoint => new(IsProduction ? ProductionEndpoint : TestEndpoint);

    public string InstallationNumber
    {
        get
        {
            if (settings.Get(VerifactuSettingKeys.InstallationNumber) is { Length: > 0 } number)
                return number;
            var created = Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
            settings.Set(VerifactuSettingKeys.InstallationNumber, created);
            return created;
        }
    }

    public VerifactuQueueStatus GetStatus()
    {
        using var db = dbFactory.CreateDbContext();
        var counts = db.VerifactuRecords.GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() }).ToList();
        int Count(VerifactuStatus s) => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0;
        var lastError = db.VerifactuRecords.Where(r => r.ErrorMessage != null).OrderByDescending(r => r.LastAttemptUtc)
            .Select(r => r.ErrorMessage).FirstOrDefault();
        return new VerifactuQueueStatus(Count(VerifactuStatus.Pending), Count(VerifactuStatus.Accepted),
            Count(VerifactuStatus.AcceptedWithErrors), Count(VerifactuStatus.Rejected), NextSendAfter(), lastError,
            Count(VerifactuStatus.NotSent));
    }

    /// <summary>VFA-05: registros más recientes, opcionalmente solo los de un estado.</summary>
    public IReadOnlyList<VerifactuRecord> GetRecent(int limit = 100, VerifactuStatus? status = null)
    {
        using var db = dbFactory.CreateDbContext();
        var query = db.VerifactuRecords.AsNoTracking();
        if (status is { } s)
            query = query.Where(r => r.Status == s);
        return query.OrderByDescending(r => r.Id).Take(limit).ToList();
    }

    /// <summary>Ids de los registros que ya tienen una subsanación que no ha sido rechazada.</summary>
    public IReadOnlySet<int> CorrectedRecordIds()
    {
        using var db = dbFactory.CreateDbContext();
        return db.VerifactuRecords.AsNoTracking()
            .Where(r => r.CorrectsRecordId != null && r.Status != VerifactuStatus.Rejected)
            .Select(r => r.CorrectsRecordId!.Value)
            .ToHashSet();
    }

    /// <summary>
    /// VFA-05: reenvía como subsanación un registro rechazado o aceptado con errores. Se genera un registro
    /// de alta nuevo de la misma factura (Subsanacion = S y, si fue rechazado, RechazoPrevio = S), encadenado
    /// al último, que entra en la cola. El original no se toca.
    /// </summary>
    public OperationResult<VerifactuRecord> CreateCorrection(int recordId, User? user)
    {
        if (!IsEnabled)
            return OperationResult<VerifactuRecord>.Fail("ErrorVerifactuNotEnabled");

        using var db = dbFactory.CreateDbContext();
        var original = db.VerifactuRecords.Find(recordId);
        if (original is null)
            return OperationResult<VerifactuRecord>.Fail("ErrorVerifactuRecordNotFound");
        if (original.Status is not (VerifactuStatus.Rejected or VerifactuStatus.AcceptedWithErrors))
            return OperationResult<VerifactuRecord>.Fail("ErrorVerifactuNothingToCorrect");
        if (db.VerifactuRecords.Any(r => r.CorrectsRecordId == recordId && r.Status != VerifactuStatus.Rejected))
            return OperationResult<VerifactuRecord>.Fail("ErrorVerifactuAlreadyCorrected");

        var previous = db.VerifactuRecords.OrderByDescending(r => r.Id).First();
        var record = new VerifactuRecord
        {
            InvoiceId = original.InvoiceId,
            PreviousRecordId = previous.Id,
            Kind = VerifactuRecordKind.Alta,
            IssuerNif = original.IssuerNif,
            IssuerName = original.IssuerName,
            InvoiceNumber = original.InvoiceNumber,
            IssueDate = original.IssueDate,
            InvoiceType = original.InvoiceType,
            TotalVat = original.TotalVat,
            Total = original.Total,
            PreviousHash = previous.Hash,
            GeneratedAt = VerifactuHash.Timestamp(clock.GetLocalNow()),
            Hash = "",
            Status = VerifactuStatus.Pending,
            CorrectsRecordId = original.Id,
            PreviouslyRejected = original.Status == VerifactuStatus.Rejected,
        };
        record.Hash = VerifactuHash.ForAlta(record.IssuerNif, record.InvoiceNumber, record.IssueDate, record.InvoiceType,
            record.TotalVat, record.Total, record.PreviousHash, record.GeneratedAt);
        db.VerifactuRecords.Add(record);
        AuditLog.Record(db, user, AuditActions.VerifactuCorrection,
            $"{original.InvoiceNumber}: subsanación del registro {original.Id} ({original.Status}, {original.ErrorCode})",
            clock.GetUtcNow().UtcDateTime, authorizedBy: null);
        db.SaveChanges();
        return OperationResult<VerifactuRecord>.Ok(record);
    }

    /// <param name="ignoreWait">Solo para "Enviar ahora" desde la pantalla: no espera al tiempo indicado por la AEAT.</param>
    public async Task<VerifactuSendResult> SendPendingAsync(bool ignoreWait = false, CancellationToken cancellationToken = default)
    {
        if (!IsEnabled)
            return VerifactuSendResult.Nothing;
        if (!ignoreWait && NextSendAfter() is { } next && next > clock.GetUtcNow().UtcDateTime)
            return VerifactuSendResult.Nothing;

        await OneAtATime.WaitAsync(cancellationToken);
        try
        {
            return await SendBatchAsync(cancellationToken);
        }
        finally
        {
            OneAtATime.Release();
        }
    }

    public async Task<string?> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        using var certificate = certificates.Load();
        if (certificate is null)
            return "ErrorVerifactuNoCertificate";
        try
        {
            await transport.TestAsync(Endpoint, certificate, cancellationToken);
            return null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or System.Security.Authentication.AuthenticationException)
        {
            throw new VerifactuServiceException(e.Message);
        }
    }

    private async Task<VerifactuSendResult> SendBatchAsync(CancellationToken cancellationToken)
    {
        using var db = dbFactory.CreateDbContext();
        var pending = db.VerifactuRecords
            .Include(r => r.Invoice).ThenInclude(i => i!.VatLines)
            .Include(r => r.PreviousRecord)
            .Where(r => r.Status == VerifactuStatus.Pending)
            .OrderBy(r => r.Id)
            .Take(VerifactuXml.MaxRecordsPerMessage)
            .ToList();
        if (pending.Count == 0)
            return VerifactuSendResult.Nothing;

        using var certificate = certificates.Load();
        if (certificate is null)
            return new VerifactuSendResult(0, 0, 0, "ErrorVerifactuNoCertificate", null);

        var replacedIds = pending.SelectMany(r => new[] { r.Invoice!.ReplacesInvoiceId, r.Invoice!.RectifiedInvoiceId }).OfType<int>().ToList();
        var replaced = db.Invoices.AsNoTracking().Where(i => replacedIds.Contains(i.Id)).ToDictionary(i => i.Id);

        // Un mensaje por obligado a emitir (normalmente uno: el NIF del negocio).
        var now = clock.GetUtcNow().UtcDateTime;
        var environment = IsProduction ? VerifactuSettingKeys.Production : VerifactuSettingKeys.Test;
        var issuerGroup = pending.GroupBy(r => (r.IssuerNif, r.IssuerName)).First();
        var batch = issuerGroup.ToList();
        var outgoing = batch.Select(r => new OutgoingRecord(r, r.Invoice!, r.PreviousRecord,
            r.Invoice!.ReplacesInvoiceId is { } id ? replaced.GetValueOrDefault(id) : null,
            r.Invoice!.RectifiedInvoiceId is { } rid ? replaced.GetValueOrDefault(rid) : null));
        var xml = VerifactuXml.Build(issuerGroup.Key.IssuerName, issuerGroup.Key.IssuerNif, outgoing, producer, InstallationNumber);

        foreach (var r in batch)
        {
            r.Attempts++;
            r.LastAttemptUtc = now;
            r.Environment = environment;
        }

        VerifactuResponse response;
        try
        {
            response = VerifactuResponseParser.Parse(await transport.PostAsync(Endpoint, xml, certificate, cancellationToken));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or VerifactuServiceException
                                       or System.Xml.XmlException or System.Security.Authentication.AuthenticationException)
        {
            // VFA-03: sin conexión o la AEAT no responde: siguen en la cola y se reintenta más tarde.
            foreach (var r in batch)
                r.ErrorMessage = e.Message;
            var attempts = batch.Max(r => r.Attempts);
            SetNextSendAfter(now + TimeSpan.FromMinutes(Math.Min(60, Math.Pow(2, attempts - 1))));
            db.SaveChanges();
            return new VerifactuSendResult(0, 0, 0, "ErrorVerifactuSend", e.Message);
        }

        var accepted = 0;
        var rejected = 0;
        var byNumber = response.Lines.GroupBy(l => l.InvoiceNumber).ToDictionary(g => g.Key, g => g.Last());
        foreach (var r in batch)
        {
            if (!byNumber.TryGetValue(r.InvoiceNumber, out var line))
                continue; // la AEAT no dice nada de él: sigue pendiente
            r.AnsweredAtUtc = now;
            r.ErrorCode = line.ErrorCode;
            r.ErrorMessage = line.ErrorMessage;
            r.Status = line.Status switch
            {
                "Correcto" => VerifactuStatus.Accepted,
                "AceptadoConErrores" => VerifactuStatus.AcceptedWithErrors,
                _ when line.ErrorCode == DuplicateErrorCode => VerifactuStatus.Accepted, // ya lo tenía (reenvío tras un corte)
                _ => VerifactuStatus.Rejected,
            };
            if (r.Status == VerifactuStatus.Rejected)
                rejected++;
            else
                accepted++;
        }

        // VFA-03: hay que esperar lo que indique la AEAT antes del siguiente envío.
        SetNextSendAfter(now + TimeSpan.FromSeconds(response.WaitSeconds));
        db.SaveChanges();
        return new VerifactuSendResult(batch.Count, accepted, rejected, null, null);
    }

    private DateTime? NextSendAfter() =>
        DateTime.TryParse(settings.Get(VerifactuSettingKeys.NextSendAfter), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var value) ? value : null;

    private void SetNextSendAfter(DateTime utc) =>
        settings.Set(VerifactuSettingKeys.NextSendAfter, utc.ToString("O", CultureInfo.InvariantCulture));
}

/// <summary>
/// VFA-03: cola en segundo plano. Cada 30 segundos intenta enviar lo pendiente (VERI*FACTU)
/// y firma los registros que no se envían (No VERI*FACTU, VFA-04).
/// </summary>
public sealed class VerifactuQueue(VerifactuSender sender, VerifactuSigner signer)
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(30);

    public event Action<Exception>? Failed;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await sender.SendPendingAsync(cancellationToken: cancellationToken);
                signer.SignPending();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                Failed?.Invoke(e); // nunca debe tumbar la app
            }
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }
}
