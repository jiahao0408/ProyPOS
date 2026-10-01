using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Core.Verifactu;
using Pos.Data;
using Pos.Localization;
using Pos.Modules.Verifactu;

namespace Pos.App.ViewModels;

public sealed record VerifactuRow(string Number, string Type, string Status, string? Error, bool IsProblem);

/// <summary>
/// VFA-01: certificado y prueba de conexión con la AEAT (pruebas o producción).
/// VFA-02/03: estado de la cola de envíos, con "Enviar ahora".
/// </summary>
public partial class VerifactuPageViewModel(
    ILocalizer localizer,
    SettingsStore settings,
    CertificateStore certificates,
    VerifactuSender sender,
    RegionFormatter formatter) : PageViewModel(localizer)
{
    [ObservableProperty]
    private bool _enabled;

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
        Enabled = sender.IsEnabled;
        Environment = Environments.First(e => e.Value == (sender.IsProduction ? VerifactuSettingKeys.Production : VerifactuSettingKeys.Test));
        RefreshCertificate();
        RefreshQueue();
    }

    [RelayCommand]
    private void Save()
    {
        if (Enabled && !certificates.HasCertificate)
        {
            ShowError("ErrorVerifactuNoCertificate");
            return;
        }
        settings.Set(VerifactuSettingKeys.Enabled, Enabled ? "true" : "false");
        settings.Set(VerifactuSettingKeys.Environment, Environment?.Value ?? VerifactuSettingKeys.Test);
        ShowInfo("Saved");
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
        settings.Set(VerifactuSettingKeys.Enabled, "false");
        Enabled = false;
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
        QueueSummary = string.Format(L["VerifactuQueueSummary"], status.Pending, status.Accepted, status.AcceptedWithErrors, status.Rejected);
        Records.Clear();
        foreach (var r in sender.GetRecent(50))
        {
            Records.Add(new VerifactuRow(r.InvoiceNumber, r.InvoiceType, L[$"VerifactuStatus{r.Status}"],
                r.ErrorCode is null && r.ErrorMessage is null ? null : $"{r.ErrorCode} {r.ErrorMessage}".Trim(),
                r.Status is VerifactuStatus.Rejected or VerifactuStatus.AcceptedWithErrors));
        }
    }
}

/// <summary>VFA-06: "Acerca de", con la declaración responsable del sistema informático de facturación.</summary>
public partial class AboutPageViewModel(ILocalizer localizer, ProducerInfo producer) : PageViewModel(localizer)
{
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
           de facturación con huella SHA-256 encadenada y código QR, y los remite a la AEAT por su servicio web.
        e) Funciona exclusivamente como sistema VERI*FACTU: sí.
        f) Permite su uso por varios obligados tributarios: no.
        g) Tipos de firma: no aplica (en modalidad VERI*FACTU los registros no se firman; se remiten a la AEAT).
        h) Productor: {Producer.Name} — NIF {Producer.Nif}
        i) Dirección de contacto: {Producer.Address}

        El productor declara que este sistema informático cumple lo dispuesto en el artículo 29.2.j) de la
        Ley 58/2003, General Tributaria, en el Reglamento aprobado por el Real Decreto 1007/2023 y en la
        Orden HAC/1177/2024, y sus normas de desarrollo.

        En {Producer.DeclarationPlace}, a {Producer.DeclarationDate}.
        """;
}
