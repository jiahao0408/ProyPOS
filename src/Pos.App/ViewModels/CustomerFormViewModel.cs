using CommunityToolkit.Mvvm.ComponentModel;
using Pos.Core.Domain;
using Pos.Core.Invoicing;
using Pos.Core.Localization;

namespace Pos.App.ViewModels;

/// <summary>
/// Datos fiscales del cliente para una factura completa (FAC-02, FAC-06).
/// Al teclear un NIF ya conocido se rellena el resto.
/// </summary>
public sealed partial class CustomerFormViewModel(ILocalizer localizer, Func<string, Customer?> findCustomer) : ObservableObject
{
    public ILocalizer L { get; } = localizer;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NifIsValid))]
    private string _nif = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _address = "";

    [ObservableProperty]
    private string _postalCode = "";

    [ObservableProperty]
    private string _city = "";

    public bool NifIsValid => NifValidator.IsValid(Nif);

    public InvoiceCustomer ToCustomer() => new(Nif, Name, Address, PostalCode, City);

    partial void OnNifChanged(string value)
    {
        if (!NifValidator.IsValid(value) || findCustomer(value) is not { } known)
            return;
        Name = known.Name;
        Address = known.Address;
        PostalCode = known.PostalCode;
        City = known.City;
    }
}
