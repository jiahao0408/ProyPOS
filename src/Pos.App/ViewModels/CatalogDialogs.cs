using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pos.Core.Domain;
using Pos.Core.Localization;
using Pos.Localization;
using Pos.Modules.Products;

namespace Pos.App.ViewModels;

public sealed record VariantOption(Product Product, string Name, string Price, string Barcode);

/// <summary>BAZ-05: al tocar un producto con variantes, el cajero elige cuál se lleva el cliente.</summary>
public sealed partial class VariantPickerViewModel(
    ILocalizer localizer,
    RegionFormatter formatter,
    Product parent,
    IReadOnlyList<Product> variants,
    Action<Product> pick,
    Action cancel) : ViewModelBase(localizer)
{
    public string ProductName { get; } = parent.Name;

    public IReadOnlyList<VariantOption> Variants { get; } = variants
        .Select(v => new VariantOption(v, v.VariantName ?? v.Name, formatter.FormatMoney(v.Price), v.Barcode ?? ""))
        .ToList();

    [RelayCommand]
    private void Pick(VariantOption option) => pick(option.Product);

    [RelayCommand]
    private void Cancel() => cancel();
}

/// <summary>
/// BAZ-06: verificador de precios. Se escanea (o se busca) un producto y se ve su precio, dónde está
/// en la tienda y sus variantes, sin añadirlo al ticket.
/// </summary>
public sealed partial class PriceCheckViewModel(
    ILocalizer localizer,
    RegionFormatter formatter,
    CatalogService catalog,
    Action close) : ViewModelBase(localizer)
{
    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasProduct))]
    private string? _productName;

    [ObservableProperty]
    private string _priceText = "";

    [ObservableProperty]
    private string _locationText = "";

    [ObservableProperty]
    private string _categoryText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasVariants))]
    private IReadOnlyList<VariantOption> _variants = [];

    public bool HasProduct => ProductName is not null;

    public bool HasVariants => Variants.Count > 0;

    /// <summary>Enter en el buscador (lo que manda el lector al final del código).</summary>
    [RelayCommand]
    private void Submit()
    {
        var text = SearchText.Trim();
        SearchText = "";
        if (text.Length == 0)
            return;

        var product = catalog.FindByBarcode(text);
        if (product is null && catalog.Search(text, limit: 2) is [var single])
            product = single;
        if (product is null)
        {
            ProductName = null;
            Variants = [];
            Message = string.Format(L["UnknownBarcode"], text);
            MessageIsError = true;
            return;
        }

        ClearMessage();
        var full = catalog.GetProduct(product.Id)!;
        ProductName = full.Name;
        PriceText = formatter.FormatMoney(full.Price);
        LocationText = full.Location ?? L["NoLocation"];
        CategoryText = full.Category?.Name ?? L["NoCategory"];
        Variants = catalog.GetVariants(full.ParentProductId ?? full.Id)
            .Select(v => new VariantOption(v, v.VariantName ?? v.Name, formatter.FormatMoney(v.Price), v.Barcode ?? ""))
            .ToList();
    }

    [RelayCommand]
    private void Close() => close();
}
