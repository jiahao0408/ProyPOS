using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Core.Security;
using Pos.Core.Verifactu;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.Verifactu;

namespace Pos.App.ViewModels;

/// <param name="CanCorrect">VFA-05: rechazado o aceptado con errores, y aún sin subsanar.</param>
public sealed record VerifactuRow(int Id, string Number, string Type, string Status, string? Error, bool IsProblem, bool CanCorrect, string? Note);

public sealed record VerifactuEventRow(string When, string Type, string Details);

/// <summary>
/// VFA-01: certificado y prueba de conexión con la AEAT (pruebas o producción).
/// VFA-02/03: estado de la cola de envíos, con "Enviar ahora".
/// VFA-04: modalidad VERI*FACTU o No VERI*FACTU (firma y registro de eventos).
/// VFA-05: panel con filtro por estado y reenvío de subsanaciones.
/// </summary>
public partial class VerifactuPageViewModel(
    ILocalizer localizer,
    SettingsStore settings,
    CertificateStore certificates,
    VerifactuSender sender,
    VerifactuSigner signer,
    VerifactuModeService modes,
    ISession session,
    RegionFormatter formatter) : PageViewModel(localizer)
{
    /// <summary>VFA-04: true = VERI*FACTU (se envía a la AEAT); false = No VERI*FACTU.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNoVeriFactu))]
    private bool _enabled;

    [ObservableProperty]
    private Choice<VerifactuStatus?>? _statusFilter;

    [ObservableProperty]
    private string _signatureSummary = "";

    public bool IsNoVeriFactu
    {
        get => !Enabled;
        set => Enabled = !value;
    }

    public IReadOnlyList<Choice<VerifactuStatus?>> StatusFilters { get; private set; } = [];

    public ObservableCollection<VerifactuEventRow> Events { get; } = [];

    partial void OnStatusFilterChanged(Choice<VerifactuStatus?>? value) => RefreshQueue();

    [ObservableProperty]
    private Choice<string>? _environment;

    [ObservableProperty]
    private string _certificatePassword = "";

    [ObservableProperty]
    private string _certificateInfo = "";

    [ObservableProperty]
    private bool _hasCertificate;

    [ObservableProperty]
    private string _queueSummary = "";

    [ObservableProperty]
    private bool _isBusy;

    public IReadOnlyList<Choice<string>> Environments { get; private set; } = [];

    public ObservableCollection<VerifactuRow> Records { get; } = [];

    /// <summary>Última operación lanzada (para esperarla en los tests).</summary>
    public Task LastOperation { get; private set; } = Task.CompletedTask;

    public override void Load()
    {
        Environments = [new(VerifactuSettingKeys.Test, L["VerifactuTest"]), new(VerifactuSettingKeys.Production, L["VerifactuProduction"])];
        OnPropertyChanged(nameof(Environments));
        StatusFilters =
        [
            new(null, L["All"]),
            .. new[] { VerifactuStatus.Pending, VerifactuStatus.Accepted, VerifactuStatus.AcceptedWithErrors, VerifactuStatus.Rejected, VerifactuStatus.NotSent }
                .Select(s => new Choice<VerifactuStatus?>(s, L[$"VerifactuStatus{s}"])),
        ];
        OnPropertyChanged(nameof(StatusFilters));
        StatusFilter = StatusFilters[0];
        Enabled = sender.IsEnabled;
        Environment = Environments.First(e => e.Value == (sender.IsProduction ? VerifactuSettingKeys.Production : VerifactuSettingKeys.Test));
        RefreshCertificate();
        RefreshQueue();
    }

    [RelayCommand]
    private void Save()
    {
        // VFA-04: el cambio de modalidad queda en la auditoría y en el registro de eventos.
        if (!Check(modes.SetMode(Enabled ? VerifactuMode.VeriFactu : VerifactuMode.NoVeriFactu, session.CurrentUser)))
        {
            Enabled = sender.IsEnabled;
            return;
        }
        settings.Set(VerifactuSettingKeys.Environment, Environment?.Value ?? VerifactuSettingKeys.Test);
        ShowInfo("Saved");
        RefreshQueue();
    }

    /// <summary>VFA-05: reenvía como subsanación un registro rechazado o aceptado con errores.</summary>
    [RelayCommand]
    private void Correct(VerifactuRow row)
    {
        var result = sender.CreateCorrection(row.Id, session.CurrentUser);
        if (!Check(result))
            return;
        Message = string.Format(L["VerifactuCorrectionQueued"], row.Number);
        MessageIsError = false;
        RefreshQueue();
    }

    /// <summary>VFA-04: firma ya los registros pendientes de firma (lo hace también la cola cada 30 s).</summary>
    [RelayCommand]
    private void SignNow()
    {
        var signed = signer.SignPending();
        if (signed == 0 && signer.UnsignedCount() > 0)
            ShowError("ErrorVerifactuNoCertificate");
        else
        {
            Message = string.Format(L["VerifactuSigned"], signed);
            MessageIsError = false;
        }
        RefreshQueue();
    }

    [RelayCommand]
    private void VerifyEvents()
    {
        if (modes.VerifyEvents() is { } broken)
        {
            Message = string.Format(L["VerifactuEventsBroken"], broken);
            MessageIsError = true;
        }
        else
            ShowInfo("VerifactuEventsOk");
    }

    /// <summary>Lo llama la vista con el fichero .pfx elegido.</summary>
    public void LoadCertificate(byte[] pfx)
    {
        var result = certificates.Save(pfx, CertificatePassword);
        CertificatePassword = "";
        if (!Check(result))
            return;
        RefreshCertificate();
        ShowInfo("CertificateLoaded");
    }

    [RelayCommand]
    private void RemoveCertificate()
    {
        certificates.Remove();
        modes.SetMode(VerifactuMode.NoVeriFactu, session.CurrentUser);
        Enabled = sender.IsEnabled;
        RefreshCertificate();
    }

    [RelayCommand]
    private void TestConnection()
    {
        settings.Set(VerifactuSettingKeys.Environment, Environment?.Value ?? VerifactuSettingKeys.Test);
        LastOperation = RunAsync(async () =>
        {
            if (await sender.TestConnectionAsync() is { } error)
                ShowError(error);
            else
                ShowInfo("VerifactuConnectionOk");
        });
    }

    [RelayCommand]
    private void SendNow() =>
        LastOperation = RunAsync(async () =>
        {
            var result = await sender.SendPendingAsync(ignoreWait: true);
            if (result.ErrorKey is { } error)
            {
                Message = L[error] + (result.ErrorDetail is null ? "" : $" ({result.ErrorDetail})");
                MessageIsError = true;
            }
            else
            {
                Message = string.Format(L["VerifactuSent"], result.Sent, result.Accepted, result.Rejected);
                MessageIsError = result.Rejected > 0;
            }
            RefreshQueue();
        });

    private async Task RunAsync(Func<Task> action)
    {
        IsBusy = true;
        try
        {
            await action();
        }
        catch (VerifactuServiceException e)
        {
            Message = string.Format(L["ErrorVerifactuConnection"], e.Message);
            MessageIsError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshCertificate()
    {
        var info = certificates.GetInfo();
        HasCertificate = info is not null;
        CertificateInfo = info is null
            ? L["NoCertificate"]
            : string.Format(L["CertificateInfo"], info.Subject, info.Nif ?? "?", formatter.FormatDate(info.ValidUntil));
    }

    private void RefreshQueue()
    {
        var status = sender.GetStatus();
        QueueSummary = string.Format(L["VerifactuQueueSummary"], status.Pending, status.Accepted, status.AcceptedWithErrors, status.Rejected)
            + (status.NotSent > 0 ? " · " + string.Format(L["VerifactuNotSentCount"], status.NotSent) : "");
        var unsigned = signer.UnsignedCount();
        SignatureSummary = unsigned == 0 ? L["VerifactuAllSigned"] : string.Format(L["VerifactuUnsigned"], unsigned);
        var corrected = sender.CorrectedRecordIds();
        Records.Clear();
        foreach (var r in sender.GetRecent(100, StatusFilter?.Value))
        {
            var problem = r.Status is VerifactuStatus.Rejected or VerifactuStatus.AcceptedWithErrors;
            string? note = r.CorrectsRecordId is not null ? L["VerifactuIsCorrection"]
                : corrected.Contains(r.Id) ? L["VerifactuCorrected"]
                : null;
            Records.Add(new VerifactuRow(r.Id, r.InvoiceNumber, r.InvoiceType, L[$"VerifactuStatus{r.Status}"],
                r.ErrorCode is null && r.ErrorMessage is null ? null : $"{r.ErrorCode} {r.ErrorMessage}".Trim(),
                problem, problem && Enabled && !corrected.Contains(r.Id), note));
        }

        Events.Clear();
        foreach (var e in modes.GetEvents(50))
            Events.Add(new VerifactuEventRow(formatter.FormatDateTime(e.AtUtc.ToLocalTime()), e.Type,
                e.UserName is null ? e.Details : $"{e.Details} ({e.UserName})"));
    }
}

/// <summary>VFA-06: "Acerca de", con la declaración responsable del sistema informático de facturación.</summary>
public partial class AboutPageViewModel(ILocalizer localizer, ProducerInfo producer, Pos.App.Updates.UpdateService updates, ISession session)
    : PageViewModel(localizer)
{
    // --- CFG-06: actualizaciones ---

    [ObservableProperty]
    private string _updateStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private Pos.App.Updates.UpdateInfo? _available;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanInstall))]
    private bool _isUpdating;

    /// <summary>Última operación lanzada (para esperarla en los tests).</summary>
    public Task LastOperation { get; private set; } = Task.CompletedTask;

    public bool CanInstall => Available is not null && !IsUpdating && session.IsAdmin;

    public override void Load()
    {
        Available = updates.Available;
        UpdateStatus = Available is { } a ? string.Format(L["UpdateAvailable"], a.Version) : "";
    }

    [RelayCommand]
    private void CheckUpdates() => LastOperation = CheckAsync();

    private async Task CheckAsync()
    {
        IsUpdating = true;
        UpdateStatus = L["UpdateChecking"];
        var result = await updates.CheckAsync();
        IsUpdating = false;
        Available = result.Update;
        UpdateStatus = result.ErrorKey is { } error ? string.Format(L[error], result.ErrorDetail)
            : result.Update is { } update ? string.Format(L["UpdateAvailable"], update.Version)
            : string.Format(L["UpdateUpToDate"], updates.CurrentVersion);
    }

    /// <summary>Descarga, comprueba, hace una copia de seguridad y lanza el instalador; la app se cierra.</summary>
    [RelayCommand]
    private void InstallUpdate() => LastOperation = InstallAsync();

    private async Task InstallAsync()
    {
        if (Available is not { } update)
            return;
        IsUpdating = true;
        UpdateStatus = L["UpdateDownloading"];
        var result = await updates.DownloadAndInstallAsync(update);
        IsUpdating = false;
        UpdateStatus = result.ErrorKey is { } error ? string.Format(L[error], result.ErrorDetail) : L["UpdateInstalling"];
    }

    public ProducerInfo Producer { get; } = producer;

    public bool IsIncomplete => !Producer.IsComplete;

    /// <summary>
    /// Texto de la declaración responsable (art. 13 de la Orden HAC/1177/2024). Va siempre en
    /// castellano: es un documento legal ante la AEAT, no un texto de la interfaz.
    /// </summary>
    public string Declaration =>
        $"""
        DECLARACIÓN RESPONSABLE DEL SISTEMA INFORMÁTICO DE FACTURACIÓN

        a) Nombre del sistema informático: {Producer.SystemName}
        b) Código identificador del sistema: {Producer.SystemId}
        c) Versión: {Producer.Version}
        d) Componentes y funcionalidades: aplicación de escritorio para Windows 11 (C#/.NET 8) con base de datos local
           cifrada (SQLite/SQLCipher). Registra ventas, emite facturas simplificadas y completas, genera los registros
           de facturación con huella SHA-256 encadenada y código QR. En la modalidad VERI*FACTU los remite a la AEAT
           por su servicio web; en la modalidad No VERI*FACTU los firma, los conserva y lleva un registro de eventos.
        e) Funciona exclusivamente como sistema VERI*FACTU: no (admite las dos modalidades).
        f) Permite su uso por varios obligados tributarios: no.
        g) Tipos de firma: XAdES Enveloped (RSA-SHA256) con el certificado del obligado tributario, para los registros
           de la modalidad No VERI*FACTU. En la modalidad VERI*FACTU los registros no se firman.
        h) Productor: {Producer.Name} — NIF {Producer.Nif}
        i) Dirección de contacto: {Producer.Address}

        El productor declara que este sistema informático cumple lo dispuesto en el artículo 29.2.j) de la
        Ley 58/2003, General Tributaria, en el Reglamento aprobado por el Real Decreto 1007/2023 y en la
        Orden HAC/1177/2024, y sus normas de desarrollo.

        En {Producer.DeclarationPlace}, a {Producer.DeclarationDate}.
        """;
}
