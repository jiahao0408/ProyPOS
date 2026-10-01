using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Localization;
using Pos.Data;
using Pos.Modules.Printing;

namespace Pos.App.ViewModels;

/// <summary>
/// IMP-03: cabecera y pie del ticket (logo, NIF, dirección, mensaje), con vista previa antes de guardar.
/// Los datos fiscales son obligatorios: sin ellos no se puede cobrar (no habría factura válida).
/// </summary>
public partial class BusinessPageViewModel(
    ILocalizer localizer,
    ProfileStore profiles,
    PrintService printing,
    AppEnvironment environment) : PageViewModel(localizer)
{
    [ObservableProperty] private string _name = "";
    [ObservableProperty] private string _nif = "";
    [ObservableProperty] private string _address = "";
    [ObservableProperty] private string _postalCode = "";
    [ObservableProperty] private string _city = "";
    [ObservableProperty] private string _phone = "";
    [ObservableProperty] private string _footerMessage = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLogo))]
    private string? _logoPath;

    [ObservableProperty]
    private string _preview = "";

    public bool HasLogo => LogoPath is not null;

    public override void Load()
    {
        var b = profiles.GetBusiness();
        (Name, Nif, Address, PostalCode, City, Phone, FooterMessage, LogoPath) =
            (b.Name, b.Nif, b.Address, b.PostalCode, b.City, b.Phone, b.FooterMessage, b.LogoPath);
        RefreshPreview();
    }

    // Vista previa en vivo mientras se escribe.
    partial void OnNameChanged(string value) => RefreshPreview();
    partial void OnNifChanged(string value) => RefreshPreview();
    partial void OnAddressChanged(string value) => RefreshPreview();
    partial void OnPostalCodeChanged(string value) => RefreshPreview();
    partial void OnCityChanged(string value) => RefreshPreview();
    partial void OnPhoneChanged(string value) => RefreshPreview();
    partial void OnFooterMessageChanged(string value) => RefreshPreview();
    partial void OnLogoPathChanged(string? value) => RefreshPreview();

    [RelayCommand]
    private void Save()
    {
        var profile = Current();
        if (profile.ValidateFiscalData() is { } error)
        {
            ShowError(error);
            return;
        }
        profiles.SaveBusiness(profile);
        ShowInfo("Saved");
    }

    [RelayCommand]
    private void RemoveLogo() => LogoPath = null;

    /// <summary>Copia el logo elegido a la carpeta de datos de la app.</summary>
    public void SetLogoFromFile(string sourcePath)
    {
        Directory.CreateDirectory(environment.DataDirectory);
        var destination = Path.Combine(environment.DataDirectory, "logo" + Path.GetExtension(sourcePath));
        File.Copy(sourcePath, destination, overwrite: true);
        LogoPath = null; // fuerza el aviso de cambio aunque la ruta sea la misma
        LogoPath = destination;
    }

    private BusinessProfile Current() => new(Name, Nif, Address, PostalCode, City, Phone, FooterMessage, LogoPath);

    private void RefreshPreview() => Preview = printing.PreviewSample(Current(), profiles.GetPrinter());
}
